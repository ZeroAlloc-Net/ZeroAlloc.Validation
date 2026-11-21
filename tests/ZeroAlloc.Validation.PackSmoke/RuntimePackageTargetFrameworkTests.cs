using System.IO.Compression;
using Xunit;

namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>
/// Guards the supported target frameworks of every packed runtime library: each ships its
/// assembly under <c>lib/net10.0/</c> and <c>lib/net11.0/</c> and under no other
/// <c>lib/&lt;tfm&gt;/</c> folder. A net8.0 or net9.0 target that came back through a csproj or
/// Directory.Build.props would otherwise pass every other check: api-compat does not fail
/// on an unneeded suppression.
/// </summary>
[Collection(PackedFeedCollection.Name)]
public sealed class RuntimePackageTargetFrameworkTests
{
    private static readonly string[] s_supportedTfms = ["net10.0", "net11.0"];

    private readonly PackedFeed _feed;

    public RuntimePackageTargetFrameworkTests(PackedFeed feed) => _feed = feed;

    [Theory]
    [InlineData("ZeroAlloc.Validation")]
    [InlineData("ZeroAlloc.Validation.Options")]
    [InlineData("ZeroAlloc.Validation.AspNetCore")]
    [InlineData("ZeroAlloc.Validation.Testing")]
    public void RuntimePackage_ShipsExactlyTheSupportedTargetFrameworks(string packageId)
    {
        using var nupkg = ZipFile.OpenRead(_feed.PackagePath(packageId));
        var entries = nupkg.Entries.Select(e => e.FullName).ToList();

        var libTfms = entries
            .Where(e => e.StartsWith("lib/", StringComparison.Ordinal))
            .Select(e => e.Split('/')[1])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(s_supportedTfms, libTfms);

        foreach (var tfm in s_supportedTfms)
            Assert.Contains($"lib/{tfm}/{packageId}.dll", entries, StringComparer.Ordinal);
    }
}
