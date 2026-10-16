using Xunit;

namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>
/// Guards the ZV9001 duplicate-generator check in <c>build/ZeroAlloc.Validation.targets</c>,
/// and that the AspNetCore package alone brings in both generators, issue #194.
/// </summary>
/// <remarks>
/// These tests used to live in their own project and read a feed at artifacts/local that only
/// ci.yml packed, so every other workflow that ran them had to repeat that pack step first. They
/// now restore from the <see cref="PackedFeed"/> the other PackSmoke tests share, which the tests
/// pack themselves.
/// </remarks>
[Collection(PackedFeedCollection.Name)]
public sealed class DuplicateGeneratorPackTests
{
    private readonly PackedFeed _feed;

    public DuplicateGeneratorPackTests(PackedFeed feed) => _feed = feed;

    [Fact]
    public void Build_Fails_With_ZV9001_When_Both_Packages_Referenced()
        => AssertDuplicateGeneratorReferenceFails("DuplicateGenerator.Both", "ZeroAlloc.Validation.Generator");

    // MSBuild's == in conditions is case-insensitive, so the ZV9001 check in
    // build/ZeroAlloc.Validation.targets must still catch a mixed-case spelling of the
    // redundant package's identity — a plausible hand-typed variant of the real one.
    [Fact]
    public void Build_Fails_With_ZV9001_When_Generator_Referenced_With_Mixed_Case()
        => AssertDuplicateGeneratorReferenceFails("DuplicateGenerator.MixedCase", "ZeroAlloc.validation.Generator");

    [Fact]
    public void Build_Succeeds_And_Generates_Validators_When_Only_AspNetCore_Package_Referenced()
    {
        Assert.True(File.Exists(_feed.PackagePath("ZeroAlloc.Validation.AspNetCore")),
            $"{_feed.PackagePath("ZeroAlloc.Validation.AspNetCore")} was not packed.");

        var dir = CreateConsumerDirectory("DuplicateGenerator.AspNetCoreOnly");
        WriteAspNetCoreOnlyConsumer(dir);

        var build = PackedFeed.RunDotnet("build -c Release", dir);

        Assert.True(build.ExitCode == 0, $"Consumer build failed.\n{build.Output}");
    }

    private void AssertDuplicateGeneratorReferenceFails(string name, string generatorPackageIdentity)
    {
        Assert.True(File.Exists(_feed.PackagePath("ZeroAlloc.Validation.Generator")),
            $"{_feed.PackagePath("ZeroAlloc.Validation.Generator")} was not packed.");

        var dir = CreateConsumerDirectory(name);
        File.WriteAllText(Path.Combine(dir, "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Validation" Version="{_feed.Version}" />
                <PackageReference Include="{generatorPackageIdentity}" Version="{_feed.Version}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), "// empty consumer\n");

        var build = PackedFeed.RunDotnet("build -c Release", dir);

        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains("ZV9001", build.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only ZeroAlloc.Validation.AspNetCore — no ZeroAlloc.Validation and no
    /// ZeroAlloc.Validation.Generator reference. ZeroAlloc.Validation.AspNetCore pulls in core
    /// ZeroAlloc.Validation transitively, which now bundles the generator itself, issue #194.
    /// Calling the generated method AND constructing the generated validator type is the
    /// assertion: if either generator did not run, this does not compile.
    /// </summary>
    private void WriteAspNetCoreOnlyConsumer(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Validation.AspNetCore" Version="{_feed.Version}" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(dir, "Program.cs"),
            """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.AddZeroAllocAspNetCoreValidation();
            var app = builder.Build();

            var validator = new CreateOrderRequestValidator();
            _ = validator.Validate(new CreateOrderRequest { Reference = "ORD-1" });

            app.Run();

            [Validate]
            public class CreateOrderRequest
            {
                [NotEmpty] public string Reference { get; set; } = "";
            }
            """);
    }

    /// <summary>
    /// A consumer directory inside the feed's work directory, which the fixture deletes, with a
    /// NuGet.config that restores from the packed feed into the feed's private packages folder.
    /// </summary>
    private string CreateConsumerDirectory(string name)
    {
        var dir = Path.Combine(Path.GetDirectoryName(_feed.FeedDirectory)!, name);
        Directory.CreateDirectory(dir);
        PackedFeed.WriteNuGetConfig(dir, _feed.FeedDirectory, _feed.PackagesFolder);
        return dir;
    }
}
