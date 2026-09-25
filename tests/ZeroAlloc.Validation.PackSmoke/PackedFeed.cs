using System.Diagnostics;
using System.Text;

namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>
/// Packs the shipped src projects once into a private local feed that consumer projects
/// restore from.
/// </summary>
public sealed class PackedFeed : IDisposable
{
    private static readonly string[] s_packedProjects =
    [
        "src/ZeroAlloc.Validation/ZeroAlloc.Validation.csproj",
        "src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj",
        "src/ZeroAlloc.Validation.Inject/ZeroAlloc.Validation.Inject.csproj",
        "src/ZeroAlloc.Validation.Options/ZeroAlloc.Validation.Options.csproj",
        "src/ZeroAlloc.Validation.AspNetCore/ZeroAlloc.Validation.AspNetCore.csproj",
    ];

    private readonly string _workDir;
    private readonly string _feed;

    public PackedFeed()
    {
        // A unique version per run, so no NuGet cache can serve an extract of an older pack.
        Version  = $"9.9.9-packsmoke-{Guid.NewGuid():N}";
        _workDir = Path.Combine(Path.GetTempPath(), $"za-validation-packsmoke-{Guid.NewGuid():N}");
        _feed    = Path.Combine(_workDir, "feed");
        Directory.CreateDirectory(_feed);

        var repoRoot = LocateRepoRoot();

        // A private artifacts path keeps this rebuild out of the repository's own bin and
        // obj. The release workflow packs with --no-build after the tests have run, so
        // anything written there by a test would be what ships.
        var artifacts = Path.Combine(_workDir, "artifacts");

        foreach (var project in s_packedProjects)
        {
            var csproj = Path.Combine(repoRoot, project);
            var pack = RunDotnet(
                $"pack \"{csproj}\" -c Release -p:PackageVersion={Version} --artifacts-path \"{artifacts}\" -o \"{_feed}\"",
                repoRoot);
            if (pack.ExitCode != 0)
                throw new InvalidOperationException($"Packing {project} failed.\n{pack.Output}");
        }
    }

    public string Version { get; }

    /// <summary>The local feed directory every consumer restores from.</summary>
    public string FeedDirectory => _feed;

    public string PackagePath(string packageId)
        => Path.Combine(_feed, $"{packageId}.{Version}.nupkg");

    /// <summary>
    /// Writes a consumer library that restores the given packages from the local feed and
    /// calls the method each of them generates, and returns its project path.
    /// </summary>
    /// <param name="name">The consumer project's name.</param>
    /// <param name="packages">Which optional packages, besides ZeroAlloc.Validation, to reference.</param>
    /// <param name="generatedAccessibility">
    /// When set, written into the consumer's own csproj as
    /// <c>&lt;ZeroAllocGeneratedAccessibility&gt;</c>, exercising the exact
    /// <c>build/*.props</c> files the packages ship (issue #193). Null leaves the property
    /// unset, today's default behavior.
    /// </param>
    public string ScaffoldConsumer(string name, ConsumerPackages packages, string? generatedAccessibility = null)
    {
        var dir = Path.Combine(_workDir, name);
        Directory.CreateDirectory(dir);

        WriteNuGetConfig(dir, _feed, Path.Combine(_workDir, "packages"));
        WriteProject(dir, name, packages, generatedAccessibility);
        WriteSource(dir, packages);

        return Path.Combine(dir, $"{name}.csproj");
    }

    /// <summary>
    /// Writes a NuGet.config into <paramref name="projectDir"/> that restores from
    /// <paramref name="feedDir"/> (falling back to nuget.org) and uses
    /// <paramref name="packagesFolder"/> as its private global packages folder, so a scaffolded
    /// consumer — this class's own, or any other PackSmoke test's — neither reads nor pollutes
    /// the machine-wide cache.
    /// </summary>
    public static void WriteNuGetConfig(string projectDir, string feedDir, string packagesFolder)
    {
        File.WriteAllText(Path.Combine(projectDir, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{packagesFolder}" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{feedDir}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
    }

    private void WriteProject(string dir, string name, ConsumerPackages packages, string? generatedAccessibility)
    {
        var references = new StringBuilder();
        AppendReference(references, packages, ConsumerPackages.Options,    "ZeroAlloc.Validation.Options");
        AppendReference(references, packages, ConsumerPackages.Inject,     "ZeroAlloc.Validation.Inject");
        AppendReference(references, packages, ConsumerPackages.AspNetCore, "ZeroAlloc.Validation.AspNetCore");

        var accessibilityProperty = generatedAccessibility is null
            ? ""
            : $"    <ZeroAllocGeneratedAccessibility>{generatedAccessibility}</ZeroAllocGeneratedAccessibility>{Environment.NewLine}";

        // ZeroAlloc.Validation bundles ZeroAlloc.Validation.Generator itself, issue #194.
        // Referencing the standalone Generator package here too would trip the ZV9001
        // duplicate-generator guard (build/ZeroAlloc.Validation.targets) and fail every
        // consumer scaffolded below, on purpose — see DuplicateGeneratorTests for that case.
        //
        // CopyLocalLockFileAssemblies: a plain class library does not copy its package
        // dependencies to its output directory by default, so GeneratedAccessibilityPackTests,
        // which loads the built consumer DLL and reflects over its generated types, would fail
        // to resolve ZeroAlloc.Validation (and friends) at reflection time. Harmless for the
        // consumers other PackSmoke tests only build and never load.
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
            {accessibilityProperty}  </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Validation" Version="{Version}" />
            {references}  </ItemGroup>
            </Project>
            """);
    }

    private void AppendReference(StringBuilder references, ConsumerPackages packages, ConsumerPackages package, string packageId)
    {
        if (packages.HasFlag(package))
            references.Append($"""    <PackageReference Include="{packageId}" Version="{Version}" />""").AppendLine();
    }

    private static void WriteSource(string dir, ConsumerPackages packages)
    {
        var calls        = new StringBuilder();
        var usingOptions = "";
        if (packages.HasFlag(ConsumerPackages.Options))
        {
            // ValidateWithZeroAlloc() lives in ZeroAlloc.Validation.Options, issue #193 point 2 —
            // only added when the Options package is actually referenced, so an
            // Inject-only/AspNetCore-only consumer does not get a using for a namespace no
            // referenced assembly defines (CS0246).
            usingOptions = "using ZeroAlloc.Validation.Options;";
            calls.AppendLine("services.AddOptions<DatabaseOptions>().ValidateWithZeroAlloc().ValidateOnStart();");
            calls.AppendLine("services.AddOptions<SmtpOptions>().ValidateWithZeroAlloc().ValidateOnStart();");
        }
        if (packages.HasFlag(ConsumerPackages.Inject))
            calls.AppendLine("services.AddZeroAllocValidators();");
        if (packages.HasFlag(ConsumerPackages.AspNetCore))
            calls.AppendLine("services.AddZeroAllocAspNetCoreValidation();");

        // Calling the generated methods is the assertion: if a generator did not run, the
        // call does not compile. A record is included because the generators used to skip
        // them.
        File.WriteAllText(Path.Combine(dir, "Wiring.cs"), $$"""
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation;
            {{usingOptions}}
            namespace Consumer;

            [Validate]
            public class DatabaseOptions
            {
                [NotEmpty] public string ConnectionString { get; set; } = "";
            }

            [Validate]
            public record SmtpOptions
            {
                [NotEmpty] public string Host { get; init; } = "";
            }

            public static class Wiring
            {
                public static void Wire(IServiceCollection services)
                {
            {{calls}}
                }
            }
            """);
    }

    public void Dispose() => TryDeleteDirectory(_workDir);

    /// <summary>
    /// Deletes a temporary directory, best effort. A file still held by a process that has not
    /// released it yet, or a read-only file, leaves the directory behind instead of failing the
    /// test run. Any other exception is a real defect and propagates.
    /// </summary>
    public static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"PackSmoke cleanup of {path} failed: {ex}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"PackSmoke cleanup of {path} failed: {ex}");
        }
    }

    private static readonly TimeSpan s_commandTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Runs a <c>dotnet</c> build or pack command. Build servers are disabled so no MSBuild
    /// node or compiler server outlives the command holding its redirected output, and a
    /// command that does not finish in time is killed and reported with its output rather
    /// than stalling the test run.
    /// </summary>
    public static (int ExitCode, string Output) RunDotnet(string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet", arguments + " --disable-build-servers")
        {
            WorkingDirectory       = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };

        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(s_commandTimeout))
        {
            process.Kill(entireProcessTree: true);
            lock (output)
                return (-1, $"dotnet {arguments} timed out after {s_commandTimeout}.{Environment.NewLine}{output}");
        }

        // The parameterless overload also waits for the redirected output to be drained.
        process.WaitForExit();

        lock (output)
            return (process.ExitCode, output.ToString());
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZeroAlloc.Validation.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}

