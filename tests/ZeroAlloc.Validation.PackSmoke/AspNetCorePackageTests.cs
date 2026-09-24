using System.IO.Compression;
using Xunit;

namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>
/// Guards issue #188: the ZeroAlloc.Validation.AspNetCore package must carry its source
/// generator, so that a consumer restoring it from a feed gets
/// <c>AddZeroAllocAspNetCoreValidation()</c> and the action filter.
/// </summary>
[Collection(PackedFeedCollection.Name)]
public sealed class AspNetCorePackageTests
{
    private readonly PackedFeed _feed;

    public AspNetCorePackageTests(PackedFeed feed) => _feed = feed;

    [Fact]
    public void AspNetCorePackage_ShipsGenerator_AsAnalyzer()
    {
        using var nupkg = ZipFile.OpenRead(_feed.PackagePath("ZeroAlloc.Validation.AspNetCore"));
        var entries = nupkg.Entries.Select(e => e.FullName).ToList();

        // Roslyn only loads assemblies under analyzers/.
        Assert.Contains("analyzers/dotnet/cs/ZeroAlloc.Validation.AspNetCore.Generator.dll", entries, StringComparer.Ordinal);

        // The runtime assembly still ships for every target framework.
        foreach (var tfm in new[] { "net8.0", "net9.0", "net10.0" })
            Assert.Contains($"lib/{tfm}/ZeroAlloc.Validation.AspNetCore.dll", entries, StringComparer.Ordinal);

        // A bundled copy of the Inject assembly would run InjectGenerator a second time for
        // anyone who also references ZeroAlloc.Validation.Inject, see the tests below.
        Assert.DoesNotContain(entries, e => e.EndsWith("/ZeroAlloc.Validation.Inject.dll", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.StartsWith("lib/", StringComparison.Ordinal)
                                            && e.Contains(".Generator.", StringComparison.Ordinal));
    }

    [Fact]
    public void Consumer_OfAspNetCorePackage_CanCallAddZeroAllocAspNetCoreValidation()
    {
        var project = _feed.ScaffoldConsumer("AspNetCoreOnly", ConsumerPackages.AspNetCore);

        var build = PackedFeed.RunDotnet($"build \"{project}\" -c Release", Path.GetDirectoryName(project)!);

        Assert.True(build.ExitCode == 0, $"Consumer build failed.\n{build.Output}");
    }

    [Fact]
    public void Consumer_OfAspNetCoreInjectAndOptionsPackages_Builds()
    {
        // All three packages emit code from ValidatorRegistrationEmitter. If the AspNetCore
        // package carried its own copy of the Inject assembly, InjectGenerator would run
        // twice and the build would fail with CS0101 and CS0111, as it did for Options, #183.
        var project = _feed.ScaffoldConsumer(
            "AspNetCoreInjectAndOptions",
            ConsumerPackages.AspNetCore | ConsumerPackages.Inject | ConsumerPackages.Options);

        var build = PackedFeed.RunDotnet($"build \"{project}\" -c Release", Path.GetDirectoryName(project)!);

        Assert.True(build.ExitCode == 0, $"Consumer build failed.\n{build.Output}");
    }
}
