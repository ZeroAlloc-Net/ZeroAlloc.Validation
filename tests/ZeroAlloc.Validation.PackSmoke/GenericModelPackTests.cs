using Xunit;

namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>
/// Guards issue #238 end to end: a consumer restoring the packed ZeroAlloc.Validation, Inject and
/// Options packages declares generic <c>[Validate]</c> models, registers closings of them through
/// the generated <c>Add…Validator&lt;…&gt;()</c> helpers and the generic
/// <c>ValidateWithZeroAlloc&lt;…&gt;()</c> overload, and validates them. An in-repo
/// ProjectReference build cannot catch a generator that the packages fail to ship, or a helper
/// that only compiles against the repository's own references. The consumer builds with
/// <c>TreatWarningsAsErrors</c>, so a warning in the generated code fails it too.
/// </summary>
[Collection(PackedFeedCollection.Name)]
public sealed class GenericModelPackTests
{
    private readonly PackedFeed _feed;

    public GenericModelPackTests(PackedFeed feed) => _feed = feed;

    [Fact]
    public void Consumer_WithGenericModels_RegistersClosingsThroughTheHelperAndValidates()
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"za-validation-packsmoke-generic-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(workDir, "GenericConsumer");
        Directory.CreateDirectory(projectDir);

        try
        {
            PackedFeed.WriteNuGetConfig(projectDir, _feed.FeedDirectory, Path.Combine(workDir, "packages"));
            WriteProject(projectDir);
            WriteModels(projectDir);
            WriteProgram(projectDir);

            var build = PackedFeed.RunDotnet("build \"GenericConsumer.csproj\" -c Release", projectDir);
            Assert.True(build.ExitCode == 0, $"Consumer build failed.\n{build.Output}");

            var dll = Path.Combine(projectDir, "bin", "Release", "net10.0", "GenericConsumer.dll");
            var run = PackedFeed.RunDotnet($"\"{dll}\"", projectDir);
            Assert.True(run.ExitCode == 0, $"Consumer run failed.\n{run.Output}");
            Assert.Contains("Title: Title must not be empty.", run.Output, StringComparison.Ordinal);
            Assert.Contains("Lines[0].Quantity: Quantity must be greater than 0.", run.Output, StringComparison.Ordinal);
            Assert.Contains("Amount: Amount must be greater than 0.", run.Output, StringComparison.Ordinal);
            Assert.Contains("Options rejected Page<Product>", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            PackedFeed.TryDeleteDirectory(workDir);
        }
    }

    private void WriteProject(string projectDir)
    {
        File.WriteAllText(Path.Combine(projectDir, "GenericConsumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Validation" Version="{_feed.Version}" />
                <PackageReference Include="ZeroAlloc.Validation.Inject" Version="{_feed.Version}" />
                <PackageReference Include="ZeroAlloc.Validation.Options" Version="{_feed.Version}" />
                <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.12" />
              </ItemGroup>
            </Project>
            """);
    }

    private static void WriteModels(string projectDir)
    {
        File.WriteAllText(Path.Combine(projectDir, "Models.cs"), """
            using System.Collections.Generic;
            using System.Numerics;
            using ZeroAlloc.Validation;

            namespace Consumer;

            public sealed class Product { }

            [Validate]
            public class Page<TItem> where TItem : class
            {
                [NotEmpty] public string Title { get; set; } = "";
                public List<Line<TItem>> Lines { get; set; } = new();
            }

            [Validate]
            public class Line<TItem> where TItem : class
            {
                [GreaterThan(0)] public int Quantity { get; set; }
            }

            [Validate]
            public class Charge<T> where T : struct, INumber<T>
            {
                [GreaterThan(0)] public T Amount { get; set; }
            }
            """);
    }

    private static void WriteProgram(string projectDir)
    {
        // Registers a reference-type closing, which composes another closing, and a value-type
        // closing through the helpers, and a closing as an options model through the overload,
        // then prints each failure. The run is the assertion that all of them were generated,
        // registered and wired.
        File.WriteAllText(Path.Combine(projectDir, "Program.cs"), """
            using System;
            using Consumer;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;

            var services = new ServiceCollection();
            services.AddPageValidator<Product>().AddChargeValidator<decimal>();
            services.AddOptions<Page<Product>>().Configure(p => p.Title = "").ValidateWithZeroAlloc();

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

            var page = new Page<Product> { Title = "" };
            page.Lines.Add(new Line<Product> { Quantity = 0 });
            var pageResult = provider.GetRequiredService<ValidatorFor<Page<Product>>>().Validate(page);
            var chargeResult = provider.GetRequiredService<ValidatorFor<Charge<decimal>>>().Validate(new Charge<decimal>());

            foreach (var failure in pageResult.Failures)
                Console.WriteLine($"{failure.PropertyName}: {failure.ErrorMessage}");
            foreach (var failure in chargeResult.Failures)
                Console.WriteLine($"{failure.PropertyName}: {failure.ErrorMessage}");

            try
            {
                _ = provider.GetRequiredService<IOptions<Page<Product>>>().Value;
                Console.WriteLine("Options accepted an invalid Page<Product>");
                return 1;
            }
            catch (OptionsValidationException)
            {
                Console.WriteLine("Options rejected Page<Product>");
            }

            return pageResult.IsValid || chargeResult.IsValid ? 1 : 0;
            """);
    }
}
