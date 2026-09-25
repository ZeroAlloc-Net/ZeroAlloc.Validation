using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// ZV0032, issue #241: a call the generated validator makes for a rule can compile and still
/// warn, as CS8604 for a possibly-null argument or CS0612 for an obsolete method. Inside the
/// generated file that warning fails a <c>TreatWarningsAsErrors</c> build, and the user cannot
/// fix or suppress it there. It is now read from the generated validator's own body, reported
/// at the attribute as ZV0032, and the generated call alone is wrapped in a pragma for exactly
/// that warning. A call that does not warn gets no pragma and no ZV0032.
/// </summary>
public class MirroredCallWarningTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using ZeroAlloc.Validation;
        namespace TestModels;

        public sealed class NotBlankAttribute : ValidationAttribute<string>
        {
            public override bool IsValid(string value) => value.Trim().Length > 0;
        }

        public static class Probe
        {
            public static string[] FailedProperties()
            {
                var result = new RequestValidator().Validate(new Request());
                var names = new string[result.Failures.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = result.Failures[i].PropertyName;
                return names;
            }
        }

        """;

    [Theory]
    // A [Must] predicate that takes string gets a string? property.
    [InlineData("", "[Must(nameof(Ok))] public string? Code { get; set; }", "public bool Ok(string value) => true;", "Must(nameof(Ok))", "instance.Ok(instance.Code)", "[Must] on 'Code'", "CS8604")]
    // An earlier [NotNull] tests the property for null, which leaves it maybe-null for the
    // predicate, although the property is declared non-nullable.
    [InlineData("", "[NotNull][Must(nameof(Ok))] public string Code { get; set; } = \"\";", "public bool Ok(string value) => true;", "Must(nameof(Ok))", "instance.Ok(instance.Code)", "[Must] on 'Code'", "CS8604")]
    [InlineData("", "[MinLength(1)][Must(nameof(Ok))] public string Code { get; set; } = \"\";", "public bool Ok(string value) => true;", "Must(nameof(Ok))", "instance.Ok(instance.Code)", "[Must] on 'Code'", "CS8604")]
    // The types match exactly, but the parameter disallows null.
    [InlineData("", "[Must(nameof(Ok))] public string? Code { get; set; }", "public bool Ok([System.Diagnostics.CodeAnalysis.DisallowNull] string? value) => true;", "Must(nameof(Ok))", "instance.Ok(instance.Code)", "[Must] on 'Code'", "CS8604")]
    // Obsolete guards and predicates.
    [InlineData("", "[NotEmpty(When = nameof(Ok))] public string? Code { get; set; }", "[Obsolete] public bool Ok() => true;", "NotEmpty(When = nameof(Ok))", "instance.Ok()", "When of [NotEmpty] on 'Code'", "CS0612")]
    [InlineData("", "[NotEmpty(Unless = nameof(Ok))] public string? Code { get; set; }", "[Obsolete(\"use another\")] public bool Ok() => false;", "NotEmpty(Unless = nameof(Ok))", "instance.Ok()", "Unless of [NotEmpty] on 'Code'", "CS0618")]
    [InlineData("", "[Must(nameof(Ok))] public string? Code { get; set; }", "[Obsolete] public bool Ok(string? value) => false;", "Must(nameof(Ok))", "instance.Ok(instance.Code)", "[Must] on 'Code'", "CS0612")]
    // A custom rule whose T is string, after a null test; ZV0021 already rejects it on a string?.
    [InlineData("", "[NotNull][NotBlank] public string Code { get; set; } = \"\";", "", "NotBlank", "__Rule_Code_1.IsValid(instance.Code)", "[NotBlank] on 'Code'", "CS8604")]
    // [SkipWhen] and [CustomValidation].
    [InlineData("[SkipWhen(nameof(Skip))]", "[NotEmpty] public string? Code { get; set; }", "[Obsolete] public bool Skip() => false;", "SkipWhen(nameof(Skip))", "instance.Skip()", "[SkipWhen] on 'Request'", "CS0612")]
    [InlineData("", "[NotEmpty] public string? Code { get; set; }", "[Obsolete][CustomValidation] public ValidationFailure[] Check() => Array.Empty<ValidationFailure>();", "CustomValidation", "instance.Check()", "[CustomValidation] on 'Check'", "CS0612")]
    public void Warning_on_a_generated_call_is_mirrored_as_ZV0032_at_the_attribute(
        string classAttributes, string property, string members, string attribute, string call, string usage, string warningId)
    {
        var source = Model(classAttributes, property, members);

        var (result, output) = RunGenerator(source);

        var zv0032 = SingleZV0032(result);
        Assert.Equal(DiagnosticSeverity.Warning, zv0032.Severity);
        Assert.Equal(attribute, SpanText(zv0032));
        Assert.StartsWith(
            $"The generated validator's call '{call}', made for {usage}, raises {warningId}: ",
            zv0032.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(result.Diagnostics.Length, WithId(result, "ZV0032").Length);

        // The generated call carries a pragma for exactly that warning, and the file no warning.
        var generated = GeneratedValidator(result);
        Assert.Contains($"#pragma warning disable {warningId} // mirrored as ZV0032", generated, StringComparison.Ordinal);
        Assert.Contains($"#pragma warning restore {warningId}", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratedWarnings(result, output));
        FailedProperties(output);
    }

    [Theory]
    // Plain predicates and custom rules whose parameter takes the property's type exactly.
    [InlineData("[Must(nameof(Ok))] public string? Code { get; set; }", "public bool Ok(string? value) => true;")]
    [InlineData("[Must(nameof(Ok))] public string Code { get; set; } = \"\";", "public bool Ok(string value) => true;")]
    [InlineData("[NotBlank] public string Code { get; set; } = \"\";", "")]
    // Under StopOnFirstFailure the predicate runs in the else branch of the null test, where the
    // property is not null.
    [InlineData("[StopOnFirstFailure][NotNull][Must(nameof(Ok))] public string? Code { get; set; }", "public bool Ok(string value) => true;")]
    // A When guard that proves the property not null.
    [InlineData("[Must(nameof(Ok), When = nameof(HasCode))] public string? Code { get; set; }",
        "public bool Ok(string value) => true; [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(Code))] public bool HasCode() => Code is not null;")]
    // A predicate that accepts null, after a null test.
    [InlineData("[NotNull][Must(nameof(Ok))] public string Code { get; set; } = \"\";", "public bool Ok(string? value) => true;")]
    public void Call_that_does_not_warn_gets_no_ZV0032_and_no_pragma(string property, string members)
    {
        var source = Model("", property, members);

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.DoesNotContain("#pragma warning disable CS", GeneratedValidator(result), StringComparison.Ordinal);
        Assert.Empty(GeneratedWarnings(result, output));
        FailedProperties(output);
    }

    [Fact]
    public void TreatWarningsAsErrors_build_fails_only_on_ZV0032()
    {
        var source = Model("", "[NotNull][Must(nameof(Ok))] public string Code { get; set; } = \"\";",
            "public bool Ok(string value) => true; [NotEmpty(When = nameof(Old))] public string? Name { get; set; } [Obsolete] public bool Old() => true;");
        var options = Options().WithGeneralDiagnosticOption(ReportDiagnostic.Error);

        var (result, output) = RunGenerator(source, options);

        // Everything the generator reports is ZV0032, raised to an error like any warning.
        Assert.Equal(2, WithId(result, "ZV0032").Length);
        Assert.All(result.Diagnostics, d =>
        {
            Assert.Equal("ZV0032", d.Id, StringComparer.Ordinal);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        });
        // The compilation itself, generated code included, has no error left.
        Assert.Empty(Errors(output));
        EmitSucceeds(output);
    }

    [Fact]
    public void Pragma_at_the_attribute_suppresses_ZV0032()
    {
        var source = Model("", """
            #pragma warning disable ZV0032 // Ok is only called when Code is known to be set.
                [NotNull][Must(nameof(Ok))] public string Code { get; set; } = "";
            #pragma warning restore ZV0032
            """, "public bool Ok(string value) => true;");

        var (result, output) = RunGenerator(source, Options().WithGeneralDiagnosticOption(ReportDiagnostic.Error));

        Assert.DoesNotContain(WithId(result, "ZV0032"), d => !d.IsSuppressed);
        Assert.Empty(Errors(output));
    }

    [Fact]
    public void NoWarn_suppresses_ZV0032()
    {
        var source = Model("", "[NotNull][Must(nameof(Ok))] public string Code { get; set; } = \"\";", "public bool Ok(string value) => true;");
        var options = Options()
            .WithGeneralDiagnosticOption(ReportDiagnostic.Error)
            .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal) { ["ZV0032"] = ReportDiagnostic.Suppress });

        var (result, output) = RunGenerator(source, options);

        Assert.DoesNotContain(WithId(result, "ZV0032"), d => !d.IsSuppressed);
        Assert.Empty(Errors(output));
    }

    [Fact]
    public void Suppressed_compiler_warning_is_not_mirrored()
    {
        // With CS8604 off for the project, the generated call does not warn either.
        var source = Model("", "[Must(nameof(Ok))] public string? Code { get; set; }", "public bool Ok(string value) => true;");
        var options = Options()
            .WithGeneralDiagnosticOption(ReportDiagnostic.Error)
            .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal) { ["CS8604"] = ReportDiagnostic.Suppress });

        var (result, output) = RunGenerator(source, options);

        Assert.Empty(WithId(result, "ZV0032"));
        Assert.DoesNotContain("#pragma warning disable CS", GeneratedValidator(result), StringComparison.Ordinal);
        EmitSucceeds(output);
    }

    [Fact]
    public void Pragma_wraps_only_the_line_that_warned()
    {
        var source = Model("", """
            [Must(nameof(Ok))] public string? Code { get; set; }
                [Must(nameof(Fine))] public string? Name { get; set; }
            """, "public bool Ok(string value) => true; public bool Fine(string? value) => true;");

        var (result, _) = RunGenerator(source);

        var lines = GeneratedValidator(result).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        int call = Array.FindIndex(lines, l => l.Contains("instance.Ok(instance.Code)", StringComparison.Ordinal));
        Assert.Equal("#pragma warning disable CS8604 // mirrored as ZV0032 at the attribute this call is made for", lines[call - 1]);
        Assert.Equal("#pragma warning restore CS8604", lines[call + 1]);
        int fine = Array.FindIndex(lines, l => l.Contains("instance.Fine(instance.Name)", StringComparison.Ordinal));
        Assert.DoesNotContain("#pragma", lines[fine - 1], StringComparison.Ordinal);
        Assert.Equal(1, lines.Count(l => l.StartsWith("#pragma warning disable CS", StringComparison.Ordinal)));
    }

    [Fact]
    public void Async_pipeline_body_carries_the_same_pragma()
    {
        // Behaviors wrap the body in lambdas, for Validate and ValidateAsync alike; each copy of
        // the call is wrapped.
        var source = Model("", "[Must(nameof(Ok))] public string? Code { get; set; }", "public bool Ok(string value) => true;") + """

            [ZeroAlloc.Pipeline.PipelineBehavior(Order = 0)]
            public class LoggingBehavior : ZeroAlloc.Pipeline.IPipelineBehavior
            {
                public static ValidationResult Handle<TModel>(TModel instance, Func<TModel, ValidationResult> next) => next(instance);
            }

            [ZeroAlloc.Pipeline.PipelineBehavior(Order = 1)]
            public class CachingBehavior : ZeroAlloc.Pipeline.IPipelineBehavior
            {
                public static System.Threading.Tasks.ValueTask<ValidationResult> Handle<TModel>(
                    TModel instance, System.Threading.CancellationToken ct,
                    Func<TModel, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<ValidationResult>> next) => next(instance, ct);
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleZV0032(result);
        Assert.Equal(result.Diagnostics.Length, WithId(result, "ZV0032").Length);
        var generated = GeneratedValidator(result);
        Assert.Contains("ValidateAsync", generated, StringComparison.Ordinal);
        Assert.Equal(2, generated.Split("#pragma warning disable CS8604").Length - 1);
        Assert.Empty(GeneratedWarnings(result, output));
        EmitSucceeds(output);
    }

    [Fact]
    public void Base_usage_is_reported_once_by_the_base_validator()
    {
        var source = Prelude + """
            [Validate]
            public class RequestBase
            {
                [Must(nameof(Ok))] public string? Code { get; set; }
                public bool Ok(string value) => true;
            }

            [Validate]
            public class Request : RequestBase
            {
                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleZV0032(result);
        Assert.Empty(GeneratedWarnings(result, output));
    }

    private static string Model(string classAttributes, string property, string members) => Prelude + $$"""
        {{classAttributes}}
        [Validate]
        public class Request
        {
            {{property}}

            {{members}}
        }
        """;

    private static string GeneratedValidator(GeneratorDriverRunResult result) =>
        GeneratorTestHelper.GetGeneratedSource(result, "TestModels.RequestValidator.g.cs");

    private static Diagnostic SingleZV0032(GeneratorDriverRunResult result)
    {
        var matches = WithId(result, "ZV0032");
        Assert.True(matches.Length == 1, "Expected exactly one ZV0032, got: "
            + string.Join("; ", result.Diagnostics.Select(d => d.ToString())));
        return matches[0];
    }

    private static Diagnostic[] WithId(GeneratorDriverRunResult result, string id) =>
        result.Diagnostics.Where(d => string.Equals(d.Id, id, StringComparison.Ordinal)).ToArray();

    private static List<Diagnostic> GeneratedWarnings(GeneratorDriverRunResult result, Compilation output)
    {
        var generated = result.GeneratedTrees.ToHashSet();
        var warnings = new List<Diagnostic>();
        foreach (var diagnostic in output.GetDiagnostics())
        {
            if (diagnostic.Severity >= DiagnosticSeverity.Warning && diagnostic.Location.SourceTree is { } tree && generated.Contains(tree))
                warnings.Add(diagnostic);
        }
        return warnings;
    }

    private static List<Diagnostic> Errors(Compilation output)
    {
        var errors = new List<Diagnostic>();
        foreach (var diagnostic in output.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error) errors.Add(diagnostic);
        }
        return errors;
    }

    private static string SpanText(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static byte[] EmitSucceeds(Compilation output)
    {
        using var peStream = new MemoryStream();
        var emit = output.Emit(peStream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())));
        return peStream.ToArray();
    }

    private static string[] FailedProperties(Compilation output)
    {
        var assembly = System.Reflection.Assembly.Load(EmitSucceeds(output));
        var probe = assembly.GetType("TestModels.Probe", throwOnError: true)!;
        return (string[])probe.GetMethod("FailedProperties")!.Invoke(null, null)!;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .ToList();
        var pipeline = typeof(ZeroAlloc.Pipeline.IPipelineBehavior).Assembly.Location;
        if (!paths.Contains(pipeline, StringComparer.OrdinalIgnoreCase)) paths.Add(pipeline);
        return paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
    }

    private static CSharpCompilationOptions Options() =>
        new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

    private static (GeneratorDriverRunResult Result, Compilation Output) RunGenerator(string source, CSharpCompilationOptions? options = null)
    {
        var compilation = CSharpCompilation.Create(
            "MirroredCallWarningTests_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences(),
            options ?? Options());

        var driver = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }
}
