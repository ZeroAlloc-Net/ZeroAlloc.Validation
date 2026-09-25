using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;
using ZeroAlloc.Validation.Options.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Guards issue #184: generated types follow the effective accessibility of the model.
/// A validator was always emitted public, so an internal [Validate] type failed with CS9338
/// and CS0051, and the options extension class exposed internal models from a public type.
/// </summary>
public class GeneratedAccessibilityTests
{
    [Fact]
    public void InternalModel_ValidatorCompiles_AndIsInternal()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.JevOptionsValidator"));
    }

    [Fact]
    public void InternalRecord_ValidatorCompiles_AndIsInternal()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal sealed record Request([property: NotEmpty] string Name);
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.RequestValidator"));
    }

    [Fact]
    public void InternalModelWithInternalNestedModel_Compiles()
    {
        // The nested model's validator is a constructor parameter of the outer one, so both
        // must end up with an accessibility the other can use.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal class Address { [NotEmpty] public string City { get; set; } = ""; }
            [Validate] internal class Customer { public Address Home { get; set; } = new(); }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.CustomerValidator"));
    }

    [Fact]
    public void PublicModel_ValidatorStaysPublic()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Public, TypeAccessibility(compilation, "MyApp.JevOptionsValidator"));
    }

    [Fact]
    public void InternalOptionsModel_ValidateWithZeroAlloc_CompilesAndIsNotPublic()
    {
        // The probe from #184: an internal options class bound with ValidateWithZeroAlloc.
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;
            namespace MyApp;
            [Validate] internal sealed class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }

            internal static class Wiring
            {
                public static void Wire(IServiceCollection services)
                    => services.AddOptions<JevOptions>().ValidateWithZeroAlloc().ValidateOnStart();
            }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator(), new OptionsValidationEmitter());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "ZeroAlloc.Validation.Options.InternalZeroAllocOptionsValidationExtensions"));

        // With no public model there is nothing for a public extension class to hold, so it
        // must not appear in the consumer's API at all.
        Assert.Null(compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions"));
    }

    [Fact]
    public void MixedOptionsModels_SplitAcrossPublicAndInternalExtensionClasses()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;
            namespace MyApp;
            [Validate] public class PublicOptions { [NotEmpty] public string Host { get; set; } = ""; }
            [Validate] internal record InternalOptions { [NotEmpty] public string Key { get; init; } = ""; }

            internal static class Wiring
            {
                public static void Wire(IServiceCollection services)
                {
                    services.AddOptions<PublicOptions>().ValidateWithZeroAlloc();
                    services.AddOptions<InternalOptions>().ValidateWithZeroAlloc();
                }
            }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator(), new OptionsValidationEmitter());

        Assert.Empty(Errors(compilation));

        var publicClass = compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions");
        Assert.NotNull(publicClass);
        Assert.Equal(Accessibility.Public, publicClass.DeclaredAccessibility);
        Assert.Equal(["PublicOptions"], ExtendedModels(publicClass));

        var internalClass = compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.InternalZeroAllocOptionsValidationExtensions");
        Assert.NotNull(internalClass);
        Assert.Equal(Accessibility.Internal, internalClass.DeclaredAccessibility);
        Assert.Equal(["InternalOptions"], ExtendedModels(internalClass));
    }

    private static List<string> ExtendedModels(INamedTypeSymbol extensionClass)
        => extensionClass.GetMembers("ValidateWithZeroAlloc")
            .OfType<IMethodSymbol>()
            .Select(m => ((INamedTypeSymbol)m.Parameters[0].Type).TypeArguments[0].Name)
            .ToList();

    private static Accessibility TypeAccessibility(Compilation compilation, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        Assert.NotNull(type);
        return type.DeclaredAccessibility;
    }

    private static List<string> Errors(Compilation compilation)
    {
        var errors = new List<string>();
        foreach (var d in compilation.GetDiagnostics())
        {
            // Id and message only: the default formatting leads with the generated file
            // path, which truncates the useful part out of an assertion failure.
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add($"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        return errors;
    }

    /// <summary>
    /// Runs the generators over <paramref name="source"/> and returns the compilation with
    /// their output added, referencing this test host's own dependencies, which include the
    /// options and dependency injection assemblies.
    /// </summary>
    private static Compilation RunAndCompile(string source, params IIncrementalGenerator[] generators)
    {
        var trusted = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(System.IO.Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver
            .Create(generators)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        return output;
    }
}
