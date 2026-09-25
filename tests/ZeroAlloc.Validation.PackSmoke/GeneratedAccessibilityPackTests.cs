using System.Reflection;
using Xunit;

namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>
/// Proves issue #193 point 1 flows through a real packed-and-restored consumer, not just an
/// in-repo ProjectReference build: the shipped build/*.props files in ZeroAlloc.Validation,
/// ZeroAlloc.Validation.Options, ZeroAlloc.Validation.Inject and ZeroAlloc.Validation.AspNetCore
/// make ZeroAllocGeneratedAccessibility CompilerVisible to each bundled generator, and setting
/// it to Internal makes every public entry point those generators emit — the model validators,
/// the options extension class, and the Inject/AspNetCore DI glue — internal instead.
/// </summary>
[Collection(PackedFeedCollection.Name)]
public sealed class GeneratedAccessibilityPackTests
{
    private readonly PackedFeed _feed;

    public GeneratedAccessibilityPackTests(PackedFeed feed) => _feed = feed;

    [Fact]
    public void Internal_Consumer_EveryGeneratedEntryPointIsNotPublic()
    {
        var project = _feed.ScaffoldConsumer(
            "AccessibilityInternal",
            ConsumerPackages.Options | ConsumerPackages.Inject | ConsumerPackages.AspNetCore,
            generatedAccessibility: "Internal");
        var projectDir = Path.GetDirectoryName(project)!;

        var build = PackedFeed.RunDotnet($"build \"{project}\" -c Release", projectDir);
        Assert.True(build.ExitCode == 0, $"Consumer build failed.\n{build.Output}");

        var assembly = Assembly.LoadFrom(Path.Combine(projectDir, "bin", "Release", "net10.0", "AccessibilityInternal.dll"));

        // The generated validators for the two public [Validate] models Wiring.cs declares.
        AssertNotPublic(assembly, "Consumer.DatabaseOptionsValidator");
        AssertNotPublic(assembly, "Consumer.SmtpOptionsValidator");

        // ZeroAlloc.Validation.Inject's DI registration extension class.
        AssertNotPublic(assembly, "ZeroAllocValidatorRegistrationExtensions");

        // ZeroAlloc.Validation.AspNetCore's MVC wiring extension class. The action filter
        // itself is already internal regardless of this property.
        AssertNotPublic(assembly, "ZeroAllocValidationServiceCollectionExtensions");

        // ZeroAlloc.Validation.Options: every model routes into the internal extensions
        // class, and the public one is never emitted.
        AssertNotPublic(assembly, "ZeroAlloc.Validation.Options.InternalZeroAllocOptionsValidationExtensions");
        Assert.Null(assembly.GetType("ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions"));
    }

    [Fact]
    public void Unset_Consumer_GeneratedEntryPointsStayPublic()
    {
        var project = _feed.ScaffoldConsumer(
            "AccessibilityUnset",
            ConsumerPackages.Options | ConsumerPackages.Inject | ConsumerPackages.AspNetCore);
        var projectDir = Path.GetDirectoryName(project)!;

        var build = PackedFeed.RunDotnet($"build \"{project}\" -c Release", projectDir);
        Assert.True(build.ExitCode == 0, $"Consumer build failed.\n{build.Output}");

        var assembly = Assembly.LoadFrom(Path.Combine(projectDir, "bin", "Release", "net10.0", "AccessibilityUnset.dll"));

        AssertPublic(assembly, "Consumer.DatabaseOptionsValidator");
        AssertPublic(assembly, "Consumer.SmtpOptionsValidator");
        AssertPublic(assembly, "ZeroAllocValidatorRegistrationExtensions");
        AssertPublic(assembly, "ZeroAllocValidationServiceCollectionExtensions");
        AssertPublic(assembly, "ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions");
        Assert.Null(assembly.GetType("ZeroAlloc.Validation.Options.InternalZeroAllocOptionsValidationExtensions"));
    }

    private static void AssertNotPublic(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName);
        Assert.NotNull(type);
        Assert.False(type!.IsVisible, $"'{typeName}' should not be public (visible outside its assembly).");
    }

    private static void AssertPublic(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName);
        Assert.NotNull(type);
        Assert.True(type!.IsVisible, $"'{typeName}' should be public (visible outside its assembly).");
    }
}
