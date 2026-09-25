using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;
using ZeroAlloc.Validation.Options.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Guards issue #220. A nested model's validator joins its containing types' names with
/// underscores, so <c>Outer.Request</c> and a top-level <c>Outer_Request</c> in the same
/// namespace would both get <c>Outer_RequestValidator</c>. The duplicate hint name failed the
/// whole generator. ZV0031 now reports each model, naming the other, and no generator emits a
/// validator or glue for either.
/// </summary>
public class ValidatorNameCollisionTests
{
    private const string Rule = """[NotEmpty] public string Name { get; set; } = "";""";

    /// <summary>Two models whose validators would share a name, and the names ZV0031 gives.</summary>
    public static TheoryData<string, string, string> Collisions() => new()
    {
        {
            $$"""
            public class Outer { [Validate] public class Request { {{Rule}} } }
            [Validate] public class Outer_Request { {{Rule}} }
            """,
            "MyApp.Outer.Request", "MyApp.Outer_Request"
        },
        {
            $$"""
            public class A { public class B_Request { } public class B { [Validate] public class Request { {{Rule}} } } }
            public class A_B { [Validate] public record Request { {{Rule}} } }
            """,
            "MyApp.A.B.Request", "MyApp.A_B.Request"
        },
        {
            $$"""
            public class A { [Validate] public readonly struct B_Request { [NotEmpty] public string? Name { get; init; } } }
            public class A_B { [Validate] public class Request { {{Rule}} } }
            """,
            "MyApp.A.B_Request", "MyApp.A_B.Request"
        },
    };

    [Theory]
    [MemberData(nameof(Collisions))]
    public void CollidingValidatorNames_ReportZV0031OnBothModels(string declarations, string first, string second)
    {
        var (compilation, diagnostics, generated) = Run(Source(declarations), new ValidatorGenerator());

        var zv0031 = diagnostics.Where(d => string.Equals(d.Id, "ZV0031", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, zv0031.Count);
        Assert.All(zv0031, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.All(zv0031, d => Assert.Equal("Validate", SourceAt(d)));

        var validatorName = ValidatorNameOf(first);
        var messages = zv0031.ConvertAll(Message);
        Assert.Contains(messages, m => string.Equals(m, Expected(first, second, validatorName), StringComparison.Ordinal));
        Assert.Contains(messages, m => string.Equals(m, Expected(second, first, validatorName), StringComparison.Ordinal));

        Assert.DoesNotContain(generated, s => s.Contains($"class {validatorName}", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "CS8785", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
    }

    [Fact]
    public void ThreeModelsWithOneValidatorName_EachNamesTheOthers()
    {
        var source = Source($$"""
            public class A { [Validate] public class B_C { {{Rule}} } public class B { [Validate] public class C { {{Rule}} } } }
            [Validate] public class A_B_C { {{Rule}} }
            """);

        var (compilation, diagnostics, generated) = Run(source, new ValidatorGenerator());

        var messages = diagnostics.Where(d => string.Equals(d.Id, "ZV0031", StringComparison.Ordinal)).Select(Message).ToList();
        Assert.Equal(3, messages.Count);
        const string expected =
            "The validator for 'MyApp.A_B_C' would be named 'A_B_CValidator', the same as the validator for "
                + "'MyApp.A.B.C', 'MyApp.A.B_C', so no validator is generated for these types; rename one of them";
        Assert.Contains(messages, m => string.Equals(m, expected, StringComparison.Ordinal));
        Assert.DoesNotContain(generated, s => s.Contains("A_B_CValidator", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
    }

    [Fact]
    public void CollidingModels_AreLeftOutOfTheGlue_OtherModelsKeepTheirs()
    {
        var source = Source($$"""
            public class Outer { [Validate] public class Request { {{Rule}} } }
            [Validate] public class Outer_Request { {{Rule}} }
            [Validate] public class Customer { {{Rule}} public Outer.Request? Nested { get; set; } }

            internal static class Wiring
            {
                public static void Wire(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions.ValidateWithZeroAlloc(
                        Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.AddOptions<Customer>(services));
                    services.AddZeroAllocValidators();
                }
            }
            """);

        var (compilation, diagnostics, generated) = Run(
            source,
            new ValidatorGenerator(),
            new OptionsValidationEmitter(),
            new global::ZeroAlloc.Validation.Inject.InjectGenerator());

        Assert.Equal(["ZV0031", "ZV0031"], diagnostics.ConvertAll(d => d.Id));
        Assert.Empty(Errors(compilation));
        Assert.DoesNotContain(generated, s => s.Contains("Outer_RequestValidator", StringComparison.Ordinal));
        Assert.Contains(generated, s => s.Contains("global::MyApp.CustomerValidator>", StringComparison.Ordinal));
    }

    [Fact]
    public void CollidingModels_AreLeftOutOfTheAspNetCoreGlue()
    {
        var source = Source($$"""
            public class Outer { [Validate] public class Request { {{Rule}} } }
            [Validate] public class Outer_Request { {{Rule}} }
            [Validate] public class Customer { {{Rule}} }
            """);

        var (_, _, generated) = Run(source, new global::ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter());

        Assert.Contains(generated, s => s.Contains("global::MyApp.CustomerValidator>", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, s => s.Contains("Request", StringComparison.Ordinal));
    }

    /// <summary>Each source has a lookalike that gets no validator of that name, so nothing collides.</summary>
    public static TheoryData<string> NoCollisions() => new()
    {
        // Not [Validate].
        $$"""public class Outer { [Validate] public class Request { {{Rule}} } } public class Outer_Request { {{Rule}} }""",
        // Generic, so it gets no validator: ZV0029.
        $$"""public class Outer { [Validate] public class Request { {{Rule}} } } [Validate] public class Outer_Request<T> { {{Rule}} }""",
        // Out of the validator's reach: ZV0025.
        $$"""public class Outer { [Validate] public class Request { {{Rule}} } } [Validate] file class Outer_Request { {{Rule}} }""",
        // Another namespace.
        $$"""public class Outer { [Validate] public class Request { {{Rule}} } } namespace Other { [Validate] public class Outer_Request { {{Rule}} } }""",
    };

    [Theory]
    [MemberData(nameof(NoCollisions))]
    public void LookalikeWithoutThatValidator_DoesNotCollide(string declarations)
    {
        var source = "using ZeroAlloc.Validation;\nnamespace MyApp\n{\n" + declarations + "\n}\n";

        var (compilation, diagnostics, _) = Run(source, new ValidatorGenerator());

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZV0031", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.Outer_RequestValidator"));
    }

    private static string Expected(string model, string other, string validatorName) =>
        $"The validator for '{model}' would be named '{validatorName}', the same as the validator for '{other}', "
            + "so no validator is generated for these types; rename one of them";

    private static string ValidatorNameOf(string displayName) =>
        displayName.Substring("MyApp.".Length).Replace('.', '_') + "Validator";

    private static string Source(string declarations) => $$"""
        using ZeroAlloc.Validation;
        namespace MyApp;

        {{declarations}}
        """;

    private static string Message(Diagnostic d) => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

    private static string SourceAt(Diagnostic diagnostic)
    {
        var tree = diagnostic.Location.SourceTree;
        Assert.NotNull(tree);
        return tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }

    private static List<string> Errors(Compilation compilation)
    {
        var errors = new List<string>();
        foreach (var d in compilation.GetDiagnostics())
        {
            // Id and message only: the default formatting leads with the generated file path.
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add($"{d.Id}: {Message(d)}");
        }
        return errors;
    }

    /// <summary>
    /// Runs the generators over <paramref name="source"/>, referencing this test host's own
    /// dependencies, and returns the updated compilation, the generator diagnostics and the text
    /// of every generated file.
    /// </summary>
    private static (Compilation Compilation, List<Diagnostic> Diagnostics, List<string> Generated) Run(
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

        var driver = CSharpGeneratorDriver.Create(generators)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generated = new List<string>();
        foreach (var result in driver.GetRunResult().Results)
        {
            foreach (var file in result.GeneratedSources)
                generated.Add(file.SourceText.ToString());
        }

        return (output, new List<Diagnostic>(diagnostics), generated);
    }
}
