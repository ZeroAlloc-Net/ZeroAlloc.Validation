using System.Diagnostics;
using System.IO;
using Xunit;

namespace ZeroAlloc.Validation.DuplicateGeneratorTests;

public sealed class DuplicateGeneratorDiagnosticTests
{
    [Fact]
    public Task Build_Fails_With_ZV9001_When_Both_Packages_Referenced()
        => AssertDuplicateGeneratorReferenceFails("ZeroAlloc.Validation.Generator");

    // MSBuild's == in conditions is case-insensitive, so the ZV9001 check in
    // build/ZeroAlloc.Validation.targets must still catch a mixed-case spelling of the
    // redundant package's identity — a plausible hand-typed variant of the real one.
    [Fact]
    public Task Build_Fails_With_ZV9001_When_Generator_Referenced_With_Mixed_Case()
        => AssertDuplicateGeneratorReferenceFails("ZeroAlloc.validation.Generator");

    private static async Task AssertDuplicateGeneratorReferenceFails(string generatorPackageIdentity)
    {
        var feed    = LocateLocalFeed();
        var version = CoreVersion(feed);

        var workDir = Path.Combine(Path.GetTempPath(), "za-validation-dup-gen-" + Path.GetRandomFileName());
        Directory.CreateDirectory(workDir);
        try
        {
            WriteNuGetConfig(workDir, feed);

            File.WriteAllText(Path.Combine(workDir, "Consumer.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="ZeroAlloc.Validation" Version="{version}" />
                    <PackageReference Include="{generatorPackageIdentity}" Version="{version}" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(workDir, "Program.cs"), "// empty consumer\n");

            var (exitCode, stdout, stderr) = await RunDotnetAsync(workDir, "build", "-c", "Release");
            Assert.NotEqual(0, exitCode);
            var combined = stdout + "\n" + stderr;
            Assert.Contains("ZV9001", combined, StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(workDir);
        }
    }

    [Fact]
    public async Task Build_Succeeds_And_Generates_Validators_When_Only_AspNetCore_Package_Referenced()
    {
        var feed    = LocateLocalFeed();
        var version = CoreVersion(feed);

        Assert.NotEmpty(Directory.GetFiles(feed, "ZeroAlloc.Validation.AspNetCore." + version + ".nupkg"));

        var workDir = Path.Combine(Path.GetTempPath(), "za-validation-aspnetcore-only-" + Path.GetRandomFileName());
        Directory.CreateDirectory(workDir);
        try
        {
            WriteNuGetConfig(workDir, feed);
            WriteAspNetCoreOnlyConsumer(workDir, version);

            var (exitCode, stdout, stderr) = await RunDotnetAsync(workDir, "build", "-c", "Release");
            Assert.True(exitCode == 0, $"Consumer build failed.\n{stdout}\n{stderr}");
        }
        finally
        {
            CleanUp(workDir);
        }
    }

    /// <summary>
    /// Only ZeroAlloc.Validation.AspNetCore — no ZeroAlloc.Validation and no
    /// ZeroAlloc.Validation.Generator reference. ZeroAlloc.Validation.AspNetCore pulls in core
    /// ZeroAlloc.Validation transitively, which now bundles the generator itself, issue #194.
    /// Calling the generated method AND constructing the generated validator type is the
    /// assertion: if either generator did not run, this does not compile.
    /// </summary>
    private static void WriteAspNetCoreOnlyConsumer(string workDir, string version)
    {
        File.WriteAllText(Path.Combine(workDir, "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Validation.AspNetCore" Version="{version}" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(workDir, "Program.cs"),
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

    private static void WriteNuGetConfig(string workDir, string feed)
    {
        File.WriteAllText(Path.Combine(workDir, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{feed}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
    }

    private static void CleanUp(string workDir)
    {
        try { Directory.Delete(workDir, recursive: true); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    private static string LocateLocalFeed()
    {
        var repoRoot = LocateRepoRoot();
        var feed     = Path.Combine(repoRoot, "artifacts", "local");
        Assert.True(Directory.Exists(feed),
            $"Local nupkg feed not found at {feed}. Run `dotnet pack -c Release -o artifacts/local` " +
            "for ZeroAlloc.Validation, ZeroAlloc.Validation.Generator, and ZeroAlloc.Validation.AspNetCore first.");
        return feed;
    }

    /// <summary>
    /// Derives the shared pack version from the core package's own nupkg filename. The
    /// "ZeroAlloc.Validation.*.nupkg" glob also matches every sub-package
    /// ("ZeroAlloc.Validation.Generator.*.nupkg", "ZeroAlloc.Validation.AspNetCore.*.nupkg", …)
    /// because `*` greedily eats the sub-package name too, so the core package is picked out
    /// by requiring the remainder after "ZeroAlloc.Validation." to start with a digit — every
    /// version does, and no sub-package name does.
    /// </summary>
    private static string CoreVersion(string feed)
    {
        const string Prefix = "ZeroAlloc.Validation.";
        var coreNupkg = Directory.GetFiles(feed, "ZeroAlloc.Validation.*.nupkg")
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith(Prefix, StringComparison.Ordinal)
                    && name.Length > Prefix.Length
                    && char.IsDigit(name[Prefix.Length]);
            })
            .ToArray();
        Assert.NotEmpty(coreNupkg);

        return Path.GetFileNameWithoutExtension(coreNupkg[0])[Prefix.Length..];
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunDotnetAsync(
        string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = workingDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync().ConfigureAwait(false);
        return (p.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZeroAlloc.Validation.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find repo root (ZeroAlloc.Validation.slnx)");
    }
}
