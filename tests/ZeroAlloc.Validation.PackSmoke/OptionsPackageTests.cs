using System.IO.Compression;
using Xunit;

namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>
/// Guards issue #183: the ZeroAlloc.Validation.Options package must carry its source
/// generator, so that a consumer restoring it from a feed gets <c>ValidateWithZeroAlloc()</c>.
/// </summary>
public sealed class OptionsPackageTests : IClassFixture<PackedFeed>
{
    private readonly PackedFeed _feed;

    public OptionsPackageTests(PackedFeed feed) => _feed = feed;

    [Fact]
    public void OptionsPackage_ShipsGenerator_AsAnalyzer()
    {
        using var nupkg = ZipFile.OpenRead(_feed.PackagePath("ZeroAlloc.Validation.Options"));
        var entries = nupkg.Entries.Select(e => e.FullName).ToList();

        // Roslyn only loads assemblies under analyzers/.
        Assert.Contains("analyzers/dotnet/cs/ZeroAlloc.Validation.Options.Generator.dll", entries, StringComparer.Ordinal);

        // A bundled copy of the Inject assembly would run InjectGenerator a second time for
        // anyone who also references ZeroAlloc.Validation.Inject, see the test below.
        Assert.DoesNotContain("analyzers/dotnet/cs/ZeroAlloc.Validation.Inject.dll", entries, StringComparer.Ordinal);
        Assert.DoesNotContain(entries, e => e.StartsWith("lib/", StringComparison.Ordinal)
                                            && e.Contains(".Generator.", StringComparison.Ordinal));
    }

    [Fact]
    public void Consumer_OfOptionsPackage_CanCallValidateWithZeroAlloc()
    {
        var project = _feed.ScaffoldConsumer("OptionsOnly", ConsumerPackages.Options);

        var build = PackedFeed.RunDotnet($"build \"{project}\" -c Release", Path.GetDirectoryName(project)!);

        Assert.True(build.ExitCode == 0, $"Consumer build failed.\n{build.Output}");
    }

    [Fact]
    public void Consumer_OfOptionsAndInjectPackages_Builds()
    {
        // docs/options.md documents combining the two. If the Options package carried its
        // own copy of the Inject assembly, the SDK would load both copies as analyzers,
        // AddZeroAllocValidators would be emitted twice, and the build would fail with
        // CS0101. That was verified while fixing #183.
        var project = _feed.ScaffoldConsumer("OptionsAndInject", ConsumerPackages.Options | ConsumerPackages.Inject);

        var build = PackedFeed.RunDotnet($"build \"{project}\" -c Release", Path.GetDirectoryName(project)!);

        Assert.True(build.ExitCode == 0, $"Consumer build failed.\n{build.Output}");
    }
}
