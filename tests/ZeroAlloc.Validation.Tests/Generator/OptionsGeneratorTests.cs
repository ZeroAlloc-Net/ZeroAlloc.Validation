using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Generator;

public class OptionsGeneratorTests
{
    [Fact]
    public void Generator_EmitsValidateWithZeroAlloc_ForValidateClass()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("ValidateWithZeroAlloc",                             generated, System.StringComparison.Ordinal);
        Assert.Contains("OptionsBuilder<global::MyApp.DatabaseOptions>",     generated, System.StringComparison.Ordinal);
        Assert.Contains("IValidateOptions<global::MyApp.DatabaseOptions>",   generated, System.StringComparison.Ordinal);
        Assert.Contains("ZeroAllocOptionsValidator<global::MyApp.DatabaseOptions>", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsValidatorFor_TryAddSingleton()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class SmtpOptions { [NotEmpty] public string Host { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::MyApp.SmtpOptions>", generated, System.StringComparison.Ordinal);
        Assert.Contains("global::MyApp.SmtpOptionsValidator",                                                   generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsTwoOverloads_ForTwoValidateClasses()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            [Validate] public class SmtpOptions     { [NotEmpty] public string Host             { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("OptionsBuilder<global::MyApp.DatabaseOptions>", generated, System.StringComparison.Ordinal);
        Assert.Contains("OptionsBuilder<global::MyApp.SmtpOptions>",     generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NonValidateType_NoOverloadEmitted()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            public class NotOptions { public string X { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.DoesNotContain("NotOptions", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NoValidateClasses_EmitsNothing()
    {
        var source = """
            namespace MyApp;
            public class Plain { public string X { get; set; } = ""; }
            """;

        var trees = RunOptionsGeneratorAllTrees(source);
        Assert.Empty(trees);
    }

    [Fact]
    public void Generator_EmitsValidateWithZeroAlloc_ForRecord()
    {
        // Regression for #183. RecordDeclarationSyntax is a sibling of
        // ClassDeclarationSyntax under TypeDeclarationSyntax, not a subtype, so a
        // class-only predicate skipped every [Validate] record and
        // ValidateWithZeroAlloc was never emitted for it. #174 was the same defect
        // in the Inject generator.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public record DatabaseOptions { [NotEmpty] public string ConnectionString { get; init; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("OptionsBuilder<global::MyApp.DatabaseOptions>", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsForRecordClass_AndSkipsValueTypes()
    {
        // OptionsBuilder<T> constrains T to a reference type, so a [Validate] struct or
        // record struct, both of which the validator generator accepts, must not get an
        // overload: it would not compile.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public record class SmtpOptions { [NotEmpty] public string Host { get; init; } = ""; }
            [Validate] public readonly record struct Tag([property: NotEmpty] string Value);
            [Validate] public readonly struct Point { [GreaterThan(0)] public int X { get; init; } }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("OptionsBuilder<global::MyApp.SmtpOptions>", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Tag", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Point", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedCode_ForClassesRecordsAndStructs_Compiles()
    {
        // Runs the validator generator and the options generator together, the way a
        // consumer's build does, and compiles the result against the real options and
        // dependency injection assemblies.
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            [Validate] public record SmtpOptions { [NotEmpty] public string Host { get; init; } = ""; }
            [Validate] public readonly record struct Tag([property: NotEmpty] string Value);

            public static class Wiring
            {
                public static void Wire(IServiceCollection services)
                {
                    services.AddOptions<DatabaseOptions>().ValidateWithZeroAlloc();
                    services.AddOptions<SmtpOptions>().ValidateWithZeroAlloc();
                }
            }
            """;

        Assert.Empty(CompileWithGenerators(source));
    }

    /// <summary>
    /// Runs the validator and options generators over <paramref name="source"/> and returns
    /// every compiler error in the resulting compilation.
    /// </summary>
    internal static System.Collections.Generic.IReadOnlyList<string> CompileWithGenerators(string source)
    {
        // The trusted platform assemblies are this test host's own dependencies, which
        // include Microsoft.Extensions.Options, dependency injection and
        // ZeroAlloc.Validation.Options through the project reference.
        var trusted = (System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(System.IO.Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver
            .Create(
                new ZeroAlloc.Validation.Generator.ValidatorGenerator(),
                new ZeroAlloc.Validation.Options.Generator.OptionsValidationEmitter())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var errors = new System.Collections.Generic.List<string>();
        foreach (var d in output.GetDiagnostics())
        {
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add(d.ToString());
        }
        return errors;
    }

    private static string RunOptionsGenerator(string source)
        => RunOptionsGeneratorAllTrees(source).First();

    private static System.Collections.Generic.IReadOnlyList<string> RunOptionsGeneratorAllTrees(string source)
    {
        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ZeroAlloc.Validation.Options.Generator.OptionsValidationEmitter();
        var driver    = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
    }
}
