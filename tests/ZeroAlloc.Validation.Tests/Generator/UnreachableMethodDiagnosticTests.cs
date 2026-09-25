using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// ZV0028: the generated validator is a separate class, so it calls a <c>[CustomValidation]</c>,
/// <c>[Must]</c>, <c>When</c> or <c>Unless</c> method as <c>instance.Method(...)</c>. A method that
/// is static, or that the validator cannot access, used to be emitted anyway and surfaced as
/// CS0176 or CS0122 inside generated code. The rule is now skipped and reported at the attribute,
/// and the rest of the model is still validated.
/// </summary>
public class UnreachableMethodDiagnosticTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using ZeroAlloc.Validation;
        namespace TestModels;

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

    private const string FailingCheck =
        "IEnumerable<ValidationFailure> Check() { yield return new ValidationFailure { PropertyName = \"Check\", ErrorMessage = \"x\" }; }";

    // A public instance method the validator can call, so ZV0013 is its only problem.
    private const string InvalidCheck = "[CustomValidation] public string Check() => \"\";";

    [Theory]
    // [CustomValidation].
    [InlineData("[CustomValidation] private " + FailingCheck,
        "CustomValidation", "Check", "[CustomValidation]", "is not accessible from it")]
    [InlineData("[CustomValidation] protected " + FailingCheck,
        "CustomValidation", "Check", "[CustomValidation]", "is not accessible from it")]
    [InlineData("[CustomValidation] private protected " + FailingCheck,
        "CustomValidation", "Check", "[CustomValidation]", "is not accessible from it")]
    [InlineData("[CustomValidation] public static " + FailingCheck,
        "CustomValidation", "Check", "[CustomValidation]", "is static")]
    [InlineData("[CustomValidation] private static " + FailingCheck,
        "CustomValidation", "Check", "[CustomValidation]", "is static")]
    // [Must].
    [InlineData("[Must(nameof(Ok))] public string? Code { get; set; } private bool Ok(string? value) => false;",
        "Must(nameof(Ok))", "Ok", "[Must] on 'Code'", "is not accessible from it")]
    [InlineData("[Must(nameof(Ok))] public string? Code { get; set; } protected bool Ok(string? value) => false;",
        "Must(nameof(Ok))", "Ok", "[Must] on 'Code'", "is not accessible from it")]
    [InlineData("[Must(nameof(Ok))] public string? Code { get; set; } private protected bool Ok(string? value) => false;",
        "Must(nameof(Ok))", "Ok", "[Must] on 'Code'", "is not accessible from it")]
    [InlineData("[Must(nameof(Ok))] public string? Code { get; set; } public static bool Ok(string? value) => false;",
        "Must(nameof(Ok))", "Ok", "[Must] on 'Code'", "is static")]
    [InlineData("[Must(nameof(Ok))] public string? Code { get; set; } private static bool Ok(string? value) => false;",
        "Must(nameof(Ok))", "Ok", "[Must] on 'Code'", "is static")]
    // When and Unless.
    [InlineData("[NotEmpty(When = nameof(Ok))] public string? Code { get; set; } private bool Ok() => true;",
        "NotEmpty(When = nameof(Ok))", "Ok", "When of [NotEmpty] on 'Code'", "is not accessible from it")]
    [InlineData("[NotEmpty(When = nameof(Ok))] public string? Code { get; set; } protected bool Ok() => true;",
        "NotEmpty(When = nameof(Ok))", "Ok", "When of [NotEmpty] on 'Code'", "is not accessible from it")]
    [InlineData("[NotEmpty(When = nameof(Ok))] public string? Code { get; set; } public static bool Ok() => true;",
        "NotEmpty(When = nameof(Ok))", "Ok", "When of [NotEmpty] on 'Code'", "is static")]
    [InlineData("[NotEmpty(Unless = nameof(Ok))] public string? Code { get; set; } private bool Ok() => false;",
        "NotEmpty(Unless = nameof(Ok))", "Ok", "Unless of [NotEmpty] on 'Code'", "is not accessible from it")]
    [InlineData("[NotEmpty(Unless = nameof(Ok))] public string? Code { get; set; } public static bool Ok() => false;",
        "NotEmpty(Unless = nameof(Ok))", "Ok", "Unless of [NotEmpty] on 'Code'", "is static")]
    public void Unreachable_method_on_the_model_reports_ZV0028_and_validates_the_rest(
        string member, string spanText, string methodName, string usage, string reason)
    {
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                {{member}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0028 = SingleDiagnostic(result, "ZV0028");
        Assert.Equal(DiagnosticSeverity.Error, zv0028.Severity);
        Assert.Equal(spanText, SpanText(zv0028));
        Assert.Equal(
            $"Method '{methodName}', used by {usage}, cannot be called from the generated validator because it {reason}",
            zv0028.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0013", StringComparison.Ordinal));
        // Emitting proves there is no CS0122 or CS0176 in generated code; only Other fails.
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("protected internal")]
    [InlineData("public")]
    public void Method_callable_from_the_assembly_is_used(string accessibility)
    {
        // Control: the validator lives in the same assembly, so internal methods are callable.
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                [Must(nameof(Ok))] public string? Code { get; set; }
                [NotEmpty(When = nameof(Yes))] public string? Name { get; set; }
                [NotEmpty(Unless = nameof(No))] public string? Other { get; set; }

                {{accessibility}} bool Ok(string? value) => false;
                {{accessibility}} bool Yes() => true;
                {{accessibility}} bool No() => false;

                [CustomValidation] {{accessibility}} {{FailingCheck}}
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Name", "Other", "Check" }, FailedProperties(output));
    }

    [Fact]
    public void Must_binds_to_the_accessible_overload()
    {
        // `instance.Ok(value)` binds to the public overload, so the private one is irrelevant.
        var source = Prelude + """
            [Validate]
            public class Request
            {
                [Must(nameof(Ok))] public string? Code { get; set; }

                public bool Ok(string? value) => false;
                private bool Ok(int value) => true;

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Must_whose_only_applicable_overload_is_private_is_reported_as_the_compiler_binds_it()
    {
        // The private overload is not a candidate from the validator, so the compiler binds to the
        // public Ok(int) and reports CS1503 for the string argument: ZV0030, quoting that error.
        var source = Prelude + """
            [Validate]
            public class Request
            {
                [Must(nameof(Ok))] public string? Code { get; set; }

                public bool Ok(int value) => true;
                private bool Ok(string? value) => false;

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0030 = SingleDiagnostic(result, "ZV0030");
        Assert.Contains("fails with CS1503", zv0030.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Public_base_method_is_used()
    {
        var source = Prelude + """
            public class RequestBase
            {
                public bool Ok(string? value) => false;
                public bool Yes() => true;

                [CustomValidation] public IEnumerable<ValidationFailure> Check()
                {
                    yield return new ValidationFailure { PropertyName = "Check", ErrorMessage = "x" };
                }
            }

            [Validate]
            public class Request : RequestBase
            {
                [Must(nameof(Ok))] public string? Code { get; set; }
                [NotEmpty(When = nameof(Yes))] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Other", "Check" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("[Must(nameof(Ok))] public string? Code { get; set; }", "protected bool Ok(string? value) => false;")]
    [InlineData("[NotEmpty(When = nameof(Ok))] public string? Code { get; set; }", "protected bool Ok() => true;")]
    [InlineData("[NotEmpty(Unless = nameof(Ok))] public string? Code { get; set; }", "private protected bool Ok() => false;")]
    public void Inaccessible_base_method_stays_ZV0017(string rule, string baseMethod)
    {
        // A base member the validator cannot reach is ZV0017's case, a warning: the base type may
        // belong to someone else. ZV0028 is not reported on top of it.
        var source = Prelude + $$"""
            public class RequestBase
            {
                {{baseMethod}}
            }

            [Validate]
            public class Request : RequestBase
            {
                {{rule}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0017 = SingleDiagnostic(result, "ZV0017");
        Assert.StartsWith("Base type member 'TestModels.RequestBase.Ok'", zv0017.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0028", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Inaccessible_base_CustomValidation_method_stays_ZV0017()
    {
        var source = Prelude + $$"""
            public class RequestBase
            {
                [CustomValidation] protected {{FailingCheck}}
            }

            [Validate]
            public class Request : RequestBase
            {
                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleDiagnostic(result, "ZV0017");
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0028", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("[CustomValidation] public static " + FailingCheck, "", "CustomValidation")]
    [InlineData("[CustomValidation] private static " + FailingCheck, "", "CustomValidation")]
    [InlineData("public static bool Ok(string? value) => false;", "[Must(nameof(Ok))] public string? Code { get; set; }", "Must(nameof(Ok))")]
    [InlineData("protected static bool Ok() => true;", "[NotEmpty(When = nameof(Ok))] public string? Code { get; set; }", "NotEmpty(When = nameof(Ok))")]
    public void Static_base_method_reports_ZV0028_not_ZV0017(string baseMember, string rule, string spanText)
    {
        // Making a static method public would not make it callable as instance.Method(), so this
        // is ZV0028 wherever the method is declared.
        var source = Prelude + $$"""
            public class RequestBase
            {
                {{baseMember}}
            }

            [Validate]
            public class Request : RequestBase
            {
                {{rule}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0028 = SingleDiagnostic(result, "ZV0028");
        Assert.Equal(spanText, SpanText(zv0028));
        Assert.EndsWith("is static", zv0028.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("[CustomValidation] private " + FailingCheck)]
    [InlineData("[Must(nameof(Ok))] public string? Code { get; set; } private bool Ok(string? value) => false;")]
    [InlineData("[NotEmpty(When = nameof(Ok))] public string? Code { get; set; } private bool Ok() => true;")]
    public void Validate_base_reports_its_own_usage_once(string member)
    {
        // RequestBase's own validator reports ZV0028. Request's validator inherits the rule and
        // drops it too, but must not add a ZV0017 for the same usage.
        var source = Prelude + $$"""
            [Validate]
            public class RequestBase
            {
                {{member}}
            }

            [Validate]
            public class Request : RequestBase
            {
                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleDiagnostic(result, "ZV0028");
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    // B does not walk A, so Request reports A's usages.
    [InlineData("", "[Validate(IncludeBaseProperties = false)]")]
    // C walks A through B, so C reports them and Request does not report them again.
    [InlineData("[Validate(IncludeBaseProperties = false)]", "[Validate]")]
    public void Usage_above_a_Validate_base_that_does_not_walk_it_is_still_reported(string bAttribute, string cAttribute)
    {
        var source = Prelude + $$"""
            public class A
            {
                [NotEmpty(When = nameof(Ok))] public string? Code { get; set; }
                [Must(nameof(Pred))] public string? Name { get; set; }

                protected bool Ok() => true;
                public static bool Pred(string? value) => false;
            }

            {{bAttribute}}
            public class B : A { }

            {{cAttribute}}
            public class C : B { }

            [Validate]
            public class Request : C
            {
                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleDiagnostic(result, "ZV0017");
        SingleDiagnostic(result, "ZV0028");
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Static_overload_on_the_model_hides_the_base_instance_method()
    {
        // C# lookup stops at Request, whose applicable Ok is static: instance.Ok(value) binds to
        // it, which is CS0176, even though RequestBase declares an instance Ok(string?).
        var source = Prelude + """
            public class RequestBase
            {
                public bool Ok(string? value) => false;
            }

            [Validate]
            public class Request : RequestBase
            {
                [Must(nameof(Ok))] public string? Code { get; set; }

                public static bool Ok(object? value) => false;

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0028 = SingleDiagnostic(result, "ZV0028");
        Assert.EndsWith("is static", zv0028.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("[CustomValidation] private IEnumerable<ValidationFailure> Check(int x) { yield break; }")]
    [InlineData("[CustomValidation] public static string Check() => \"\";")]
    public void Invalid_CustomValidation_signature_reports_ZV0013_only(string member)
    {
        // One method, one diagnostic: the signature is checked first.
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                {{member}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleDiagnostic(result, "ZV0013");
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0028", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    // No [Validate] base: Request's generation is the only one that sees the method.
    [InlineData("")]
    // RequestBase's own generation reports it, so Request must not report it again.
    [InlineData("[Validate]")]
    public void Invalid_CustomValidation_signature_on_a_base_type_reports_ZV0013_once(string baseAttribute)
    {
        var source = Prelude + $$"""
            {{baseAttribute}}
            public class RequestBase
            {
                {{InvalidCheck}}
            }

            [Validate]
            public class Request : RequestBase
            {
                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Equal("CustomValidation", SpanText(SingleDiagnostic(result, "ZV0013")));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    // B does not walk A, so Request reports A's method.
    [InlineData("[Validate(IncludeBaseProperties = false)]", "")]
    [InlineData("", "[Validate(IncludeBaseProperties = false)]")]
    // C walks A through B, so C reports it and Request does not report it again.
    [InlineData("[Validate(IncludeBaseProperties = false)]", "[Validate]")]
    public void Invalid_CustomValidation_signature_above_a_Validate_base_that_does_not_walk_it_reports_ZV0013_once(
        string bAttribute, string cAttribute)
    {
        var source = Prelude + $$"""
            public class A
            {
                {{InvalidCheck}}
            }

            {{bAttribute}}
            public class B : A { }

            {{cAttribute}}
            public class C : B { }

            [Validate]
            public class Request : C
            {
                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Equal("CustomValidation", SpanText(SingleDiagnostic(result, "ZV0013")));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Each_unreachable_usage_is_reported()
    {
        var source = Prelude + """
            [Validate]
            public class Request
            {
                [NotEmpty(When = nameof(Ok), Unless = nameof(No))] public string? Code { get; set; }

                private bool Ok() => true;
                public static bool No() => false;

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Equal(2, result.Diagnostics.Count(d => string.Equals(d.Id, "ZV0028", StringComparison.Ordinal)));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    private static Diagnostic SingleDiagnostic(GeneratorDriverRunResult result, string id)
    {
        var matches = result.Diagnostics
            .Where(d => string.Equals(d.Id, id, StringComparison.Ordinal))
            .ToArray();
        Assert.True(matches.Length == 1, $"Expected exactly one {id}, got: "
            + string.Join("; ", result.Diagnostics.Select(d => d.ToString())));
        return matches[0];
    }

    private static string SpanText(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    /// <summary>
    /// Emits the generator's output compilation, which proves the generated validator compiles,
    /// then runs it against a default <c>Request</c> and returns the property names that failed.
    /// </summary>
    private static string[] FailedProperties(Compilation output)
    {
        using var peStream = new MemoryStream();
        var emit = output.Emit(peStream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())));

        var assembly = System.Reflection.Assembly.Load(peStream.ToArray());
        var probe = assembly.GetType("TestModels.Probe", throwOnError: true)!;
        return (string[])probe.GetMethod("FailedProperties")!.Invoke(null, null)!;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

    private static (GeneratorDriverRunResult Result, Compilation Output) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "UnreachableMethodTests_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }
}
