using System.Reflection;
using Xunit;

namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>
/// Guards issue #196 end to end: a consumer that restores only the packed ZeroAlloc.Validation
/// package can derive a reusable rule attribute from ValidationAttribute&lt;T&gt;, put
/// [RuleMessage] on it, decorate a [Validate] model with it, and get back a validator the
/// generator emits a statically bound check into. An in-repo ProjectReference build cannot
/// catch a packaging gap here — the base types, or the generator's custom-rule recognition,
/// failing to ship in the .nupkg.
/// </summary>
[Collection(PackedFeedCollection.Name)]
public sealed class CustomRulePackTests
{
    private readonly PackedFeed _feed;

    public CustomRulePackTests(PackedFeed feed) => _feed = feed;

    [Fact]
    public void Consumer_WithCustomRuleAttribute_BuildsAndValidates()
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"za-validation-packsmoke-customrule-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(workDir, "CustomRuleConsumer");
        Directory.CreateDirectory(projectDir);

        try
        {
            PackedFeed.WriteNuGetConfig(projectDir, _feed.FeedDirectory, Path.Combine(workDir, "packages"));
            WriteProject(projectDir);
            WriteSource(projectDir);

            var build = PackedFeed.RunDotnet("build \"CustomRuleConsumer.csproj\" -c Release", projectDir);
            Assert.True(build.ExitCode == 0, $"Consumer build failed.\n{build.Output}");

            var dll = Path.Combine(projectDir, "bin", "Release", "net10.0", "CustomRuleConsumer.dll");
            Assert.True(File.Exists(dll), $"Expected build output at {dll}.\n{build.Output}");

            // The custom rule is rebuilt once as a static field on the generated validator, not
            // resolved through reflection or Activator at validation time. Confirm that field
            // exists before relying on the run below to prove behavior.
            var assembly = Assembly.LoadFrom(dll);
            var validatorType = assembly.GetType("Consumer.ModelValidator");
            Assert.NotNull(validatorType);
            var ruleField = validatorType!
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(f => string.Equals(f.FieldType.FullName, "Consumer.NotBlankAttribute", StringComparison.Ordinal));
            Assert.NotNull(ruleField);

            var run = PackedFeed.RunDotnet($"\"{dll}\"", projectDir);
            Assert.True(run.ExitCode == 0, $"Consumer run failed.\n{run.Output}");
            Assert.Contains("Name must not be blank.", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            PackedFeed.TryDeleteDirectory(workDir);
        }
    }

    private void WriteProject(string projectDir)
    {
        // References ZeroAlloc.Validation only — the generator it bundles is what has to
        // recognize the custom rule. CopyLocalLockFileAssemblies copies its dependencies into
        // the output directory, needed both to run the built exe and to reflect over its DLL.
        File.WriteAllText(Path.Combine(projectDir, "CustomRuleConsumer.csproj"), $"""
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
              </ItemGroup>
            </Project>
            """);
    }

    private static void WriteSource(string projectDir)
    {
        File.WriteAllText(Path.Combine(projectDir, "NotBlankAttribute.cs"), """
            using ZeroAlloc.Validation;

            namespace Consumer;

            [RuleMessage("{PropertyName} must not be blank.")]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }
            """);

        File.WriteAllText(Path.Combine(projectDir, "Model.cs"), """
            using ZeroAlloc.Validation;

            namespace Consumer;

            [Validate]
            public sealed class Model
            {
                [NotBlank]
                public string? Name { get; set; }
            }
            """);

        // Validates a model that fails the custom rule, prints the resulting failure message,
        // and exits 1 if validation unexpectedly passed — the run itself is the assertion that
        // the packed generator both recognized [NotBlank] and wired up [RuleMessage].
        File.WriteAllText(Path.Combine(projectDir, "Program.cs"), """
            using System;
            using Consumer;

            var model = new Model { Name = "  " };
            var result = new ModelValidator().Validate(model);

            if (result.IsValid)
            {
                Console.Error.WriteLine("Expected the model to fail validation, but it passed.");
                return 1;
            }

            foreach (var failure in result.Failures)
                Console.WriteLine(failure.ErrorMessage);

            return 0;
            """);
    }
}
