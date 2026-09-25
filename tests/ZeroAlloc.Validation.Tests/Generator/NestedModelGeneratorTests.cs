using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Guards issue #207: a <c>[Validate]</c> model nested in another type. The validator is a
/// top-level class in the model's namespace, so it must name the model by its fully qualified
/// name, and its own name and hint name must include the containing types, or two nested
/// models with the same simple name collide. The runtime counterpart is
/// <c>Integration/NestedModelTests</c>.
/// </summary>
public class NestedModelGeneratorTests
{
    private const string OneLevel = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        public sealed class Outer
        {
            [Validate]
            public sealed class Request
            {
                [NotEmpty] public string? Name { get; init; }
            }
        }
        """;

    [Fact]
    public void OneLevel_NestedModel_Compiles_WithContainerQualifiedValidator()
    {
        var (output, sources) = RunAndCompile(OneLevel, new ValidatorGenerator());

        Assert.Empty(Errors(output));
        Assert.NotNull(output.GetTypeByMetadataName("TestModels.Outer_RequestValidator"));
        var validator = Single(sources, "TestModels.Outer_RequestValidator.g.cs");
        Assert.Contains(
            "sealed partial class Outer_RequestValidator : ValidatorFor<global::TestModels.Outer.Request>",
            validator, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoLevel_NestedModel_Compiles()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public static class A
            {
                public sealed class B
                {
                    [Validate]
                    public sealed class Request
                    {
                        [GreaterThan(0)] public int Quantity { get; init; }
                    }
                }
            }
            """;

        var (output, sources) = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(output));
        Assert.NotNull(output.GetTypeByMetadataName("TestModels.A_B_RequestValidator"));
        Assert.Contains(sources, s => s.HintName.EndsWith("TestModels.A_B_RequestValidator.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void SameNamedNestedModels_InTwoContainers_DoNotCollide()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class First
            {
                [Validate] public sealed class Request { [NotEmpty] public string? Name { get; init; } }
            }

            public sealed class Second
            {
                [Validate] public sealed class Request { [GreaterThan(0)] public int Quantity { get; init; } }
            }

            [Validate]
            public sealed class Request { [NotEmpty] public string? Id { get; init; } }
            """;

        var (output, _) = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(output));
        Assert.NotNull(output.GetTypeByMetadataName("TestModels.First_RequestValidator"));
        Assert.NotNull(output.GetTypeByMetadataName("TestModels.Second_RequestValidator"));
        Assert.NotNull(output.GetTypeByMetadataName("TestModels.RequestValidator"));
    }

    [Fact]
    public void SameNamedContainers_InTwoNamespaces_DoNotCollideOnHintName()
    {
        var source = """
            using ZeroAlloc.Validation;

            namespace Ns1
            {
                public sealed class Outer
                {
                    [Validate] public sealed class Request { [NotEmpty] public string? Name { get; init; } }
                }
            }

            namespace Ns2
            {
                public sealed class Outer
                {
                    [Validate] public sealed class Request { [NotEmpty] public string? Name { get; init; } }
                }
            }
            """;

        var (output, sources) = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(output));
        Assert.Contains(sources, s => s.HintName.EndsWith("Ns1.Outer_RequestValidator.g.cs", StringComparison.Ordinal));
        Assert.Contains(sources, s => s.HintName.EndsWith("Ns2.Outer_RequestValidator.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void SameNamedTopLevelModels_InTwoNamespaces_DoNotCollideOnHintName()
    {
        // The hint name was {Model}Validator.g.cs, without the namespace, so this failed the
        // whole generator with a duplicate hint name even before any model was nested.
        var source = """
            using ZeroAlloc.Validation;

            namespace Ns1
            {
                [Validate] public sealed class Request { [NotEmpty] public string? Name { get; init; } }
            }

            namespace Ns2
            {
                [Validate] public sealed class Request { [NotEmpty] public string? Name { get; init; } }
            }
            """;

        var (output, sources) = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(output));
        Assert.Contains(sources, s => s.HintName.EndsWith("Ns1.RequestValidator.g.cs", StringComparison.Ordinal));
        Assert.Contains(sources, s => s.HintName.EndsWith("Ns2.RequestValidator.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalNamespace_NestedModel_Compiles()
    {
        var source = """
            using ZeroAlloc.Validation;

            public sealed class Outer
            {
                [Validate] public sealed class Request { [NotEmpty] public string? Name { get; init; } }
            }
            """;

        var (output, sources) = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(output));
        Assert.NotNull(output.GetTypeByMetadataName("Outer_RequestValidator"));
        Assert.Contains(sources, s => string.Equals(s.HintName, "Outer_RequestValidator.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedModel_AsMemberAndCollectionElement_ComposesQualifiedValidators()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class Outer
            {
                [Validate] public sealed class Line { [GreaterThan(0)] public int Quantity { get; init; } }

                public sealed class Plain { public int Value { get; init; } }

                public sealed class PlainChecker : ValidatorFor<Plain>
                {
                    public override ValidationResult Validate(Plain instance) =>
                        new ValidationResult(System.Array.Empty<ValidationFailure>());
                }
            }

            [Validate]
            public sealed class Order
            {
                public Outer.Line First { get; init; } = new();
                public List<Outer.Line> Lines { get; init; } = new();
                [ValidateWith(typeof(Outer.PlainChecker))] public Outer.Plain Checked { get; init; } = new();
            }
            """;

        var (output, sources) = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(output));
        var order = Single(sources, "TestModels.OrderValidator.g.cs");
        Assert.Contains("global::ZeroAlloc.Validation.ValidatorFor<global::TestModels.Outer.Line> firstValidator", order, StringComparison.Ordinal);
        Assert.Contains("global::ZeroAlloc.Validation.ValidatorFor<global::TestModels.Outer.Line> linesValidator", order, StringComparison.Ordinal);
        Assert.Contains("global::TestModels.Outer.PlainChecker checkedValidator", order, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedModel_InjectAndOptionsGlue_Compiles()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class First
            {
                [Validate] public sealed class Settings { [NotEmpty] public string? Name { get; set; } }
            }

            public sealed class Second
            {
                [Validate] public sealed class Settings { [NotEmpty] public string? Name { get; set; } }
            }
            """;

        var (output, sources) = RunAndCompile(
            source,
            new ValidatorGenerator(),
            new global::ZeroAlloc.Validation.Inject.InjectGenerator(),
            new global::ZeroAlloc.Validation.Options.Generator.OptionsValidationEmitter());

        Assert.Empty(Errors(output));
        var inject = Single(sources, "ZeroAllocValidatorRegistrationExtensions.g.cs");
        Assert.Contains(
            "TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::TestModels.First.Settings>, global::TestModels.First_SettingsValidator>",
            inject, StringComparison.Ordinal);
        Assert.Contains(
            "TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::TestModels.Second.Settings>, global::TestModels.Second_SettingsValidator>",
            inject, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedModel_AspNetCoreGlue_NamesQualifiedTypes()
    {
        // The ASP.NET Core glue is compiled and run for real in ZeroAlloc.Validation.Tests.AspNetCore;
        // this host does not reference ASP.NET Core, so only the emitted text is checked here.
        var (_, sources) = RunAndCompile(
            OneLevel,
            new global::ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter());

        var extensions = Single(sources, "ZeroAllocValidationServiceCollectionExtensions.g.cs");
        Assert.Contains(
            "TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::TestModels.Outer.Request>, global::TestModels.Outer_RequestValidator>",
            extensions, StringComparison.Ordinal);
        var filter = Single(sources, "ZeroAllocValidationActionFilter.g.cs");
        Assert.Contains("case global::TestModels.Outer.Request ", filter, StringComparison.Ordinal);
    }

    private static string Single(IReadOnlyList<GeneratedSourceResult> sources, string hintNameSuffix) =>
        sources.Single(s => s.HintName.EndsWith(hintNameSuffix, StringComparison.Ordinal)).SourceText.ToString();

    private static List<string> Errors(Compilation compilation)
    {
        var errors = new List<string>();
        foreach (var d in compilation.GetDiagnostics())
        {
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add($"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        return errors;
    }

    /// <summary>
    /// Runs the generators and returns the compilation with their output added, together with
    /// every generated source. A generator exception, such as a duplicate hint name, is reported
    /// by the driver as an error diagnostic and fails the test here.
    /// </summary>
    private static (Compilation Output, IReadOnlyList<GeneratedSourceResult> Sources) RunAndCompile(
        string source, params IIncrementalGenerator[] generators)
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

        var driver = CSharpGeneratorDriver
            .Create(generators)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generatorErrors = new List<string>();
        foreach (var d in diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Error)
                generatorErrors.Add($"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        Assert.Empty(generatorErrors);

        var sources = driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).ToList();
        return (output, sources);
    }
}
