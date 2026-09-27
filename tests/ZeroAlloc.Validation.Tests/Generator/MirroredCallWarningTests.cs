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
    // An obsolete property read for a predicate or a custom rule, and an override of one.
    [InlineData("", "[Obsolete][Must(nameof(Ok))] public string Code { get; set; } = \"\";", "public bool Ok(string value) => true;", "Must(nameof(Ok))", "instance.Ok(instance.Code)", "[Must] on 'Code'", "CS0612")]
    [InlineData("", "[Obsolete][NotBlank] public string Code { get; set; } = \"\";", "", "NotBlank", "__Rule_Code_0.IsValid(instance.Code)", "[NotBlank] on 'Code'", "CS0612")]
    // The same with a message, CS0618, issue #265: a warning-level obsolete argument keeps its
    // pragma, and only an error-level one is left out.
    [InlineData("", "[Obsolete(\"use another\")][Must(nameof(Ok))] public string Code { get; set; } = \"\";", "public bool Ok(string value) => true;", "Must(nameof(Ok))", "instance.Ok(instance.Code)", "[Must] on 'Code'", "CS0618")]
    [InlineData("", "[Obsolete(\"use another\")][NotBlank] public string Code { get; set; } = \"\";", "", "NotBlank", "__Rule_Code_0.IsValid(instance.Code)", "[NotBlank] on 'Code'", "CS0618")]
    [InlineData("", "[NotEmpty] public string? Code { get; set; }", "[Obsolete(\"use another\")][CustomValidation] public ValidationFailure[] Check() => Array.Empty<ValidationFailure>();", "CustomValidation", "instance.Check()", "[CustomValidation] on 'Check'", "CS0618")]
    [InlineData("", "[Must(nameof(Ok))] public override string Code { get; set; } = \"\";", "public bool Ok(string value) => true;", "Must(nameof(Ok))", "instance.Ok(instance.Code)", "[Must] on 'Code'", "CS0612", "[Obsolete] public virtual string Code { get; set; } = \"\";")]
    // A custom rule whose T is string, after a null test; ZV0021 already rejects it on a string?.
    [InlineData("", "[NotNull][NotBlank] public string Code { get; set; } = \"\";", "", "NotBlank", "__Rule_Code_1.IsValid(instance.Code)", "[NotBlank] on 'Code'", "CS8604")]
    // [SkipWhen] and [CustomValidation].
    [InlineData("[SkipWhen(nameof(Skip))]", "[NotEmpty] public string? Code { get; set; }", "[Obsolete] public bool Skip() => false;", "SkipWhen(nameof(Skip))", "instance.Skip()", "[SkipWhen] on 'Request'", "CS0612")]
    [InlineData("", "[NotEmpty] public string? Code { get; set; }", "[Obsolete][CustomValidation] public ValidationFailure[] Check() => Array.Empty<ValidationFailure>();", "CustomValidation", "instance.Check()", "[CustomValidation] on 'Check'", "CS0612")]
    // A built-in rule makes no call of its own: it reads the property directly in its
    // condition, e.g. "instance.Code is null", and that read alone can warn — issue #255.
    [InlineData("", "[NotEmpty][Obsolete] public string? Code { get; set; }", "", "NotEmpty", "string.IsNullOrEmpty(instance.Code)", "[NotEmpty] on 'Code'", "CS0612")]
    [InlineData("", "[NotEmpty][Obsolete(\"use another\")] public string? Code { get; set; }", "", "NotEmpty", "string.IsNullOrEmpty(instance.Code)", "[NotEmpty] on 'Code'", "CS0618")]
    // A condition that reads the property more than once, such as a length check's null guard,
    // is covered by one site for the whole condition, not matched piecemeal.
    [InlineData("", "[MinLength(1)][Obsolete] public string? Code { get; set; }", "", "MinLength(1)",
        "instance.Code is not null && (instance.Code.Length < 1)", "[MinLength] on 'Code'", "CS0612")]
    // An override of an obsolete property.
    [InlineData("", "[NotEmpty] public override string? Code { get; set; }", "", "NotEmpty", "string.IsNullOrEmpty(instance.Code)", "[NotEmpty] on 'Code'", "CS0612", "[Obsolete] public virtual string? Code { get; set; }")]
    public void Warning_on_a_generated_call_is_mirrored_as_ZV0032_at_the_attribute(
        string classAttributes, string property, string members, string attribute, string call, string usage, string warningId,
        string baseMembers = "")
    {
        var source = Model(classAttributes, property, members, baseMembers);

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
    // A plain built-in rule on an ordinary property makes no call and reads nothing obsolete.
    [InlineData("[NotEmpty] public string? Code { get; set; }", "")]
    [InlineData("[MinLength(1)][MaxLength(10)] public string? Code { get; set; }", "")]
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
    public void Severity_set_in_editorconfig_is_honoured()
    {
        // .editorconfig and global analyzer configs apply by file path, and the probe file has
        // none: the model's own file stands in for the generated file beside it.
        var source = Model("", "[Must(nameof(Ok))] public string? Code { get; set; }", "public bool Ok(string value) => true;");
        var options = Options()
            .WithGeneralDiagnosticOption(ReportDiagnostic.Error)
            .WithSyntaxTreeOptionsProvider(new ConfiguredSeverities(perTreeId: "CS8604", perTree: ReportDiagnostic.Suppress));

        var (result, output) = RunGenerator(source, options);

        Assert.Empty(WithId(result, "ZV0032"));
        Assert.DoesNotContain("#pragma warning disable CS", GeneratedValidator(result), StringComparison.Ordinal);
        Assert.Empty(Errors(output));
    }

    [Fact]
    public void Severity_set_in_a_global_config_is_honoured()
    {
        var source = Model("", "[Must(nameof(Ok))] public string? Code { get; set; }", "public bool Ok(string value) => true;");
        var options = Options()
            .WithSyntaxTreeOptionsProvider(new ConfiguredSeverities(globalId: "CS8604", global: ReportDiagnostic.Suppress));

        var (result, _) = RunGenerator(source, options);

        Assert.Empty(WithId(result, "ZV0032"));
        Assert.DoesNotContain("#pragma warning disable CS", GeneratedValidator(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Warning_raised_to_an_error_in_editorconfig_is_mirrored_as_an_error()
    {
        var source = Model("", "[Must(nameof(Ok))] public string? Code { get; set; }", "public bool Ok(string value) => true;");
        var options = Options()
            .WithSyntaxTreeOptionsProvider(new ConfiguredSeverities(perTreeId: "CS8604", perTree: ReportDiagnostic.Error));

        var (result, output) = RunGenerator(source, options);

        Assert.Equal(DiagnosticSeverity.Error, SingleZV0032(result).Severity);
        Assert.Empty(GeneratedWarnings(result, output));
    }

    [Fact]
    public void Experimental_api_is_mirrored_as_an_error()
    {
        // An [Experimental] API's diagnostic is an error unless the user opts in. Mirrored as a
        // warning, the pragma around the generated call would let the build through.
        var source = Model("", "[Must(nameof(Ok))] public string? Code { get; set; }",
            "[System.Diagnostics.CodeAnalysis.Experimental(\"ZX001\")] public bool Ok(string? value) => true;");

        var (result, output) = RunGenerator(source);

        var zv0032 = SingleZV0032(result);
        Assert.Equal(DiagnosticSeverity.Error, zv0032.Severity);
        Assert.Contains("raises ZX001: ", zv0032.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(GeneratedWarnings(result, output));
    }

    [Fact]
    public void Suppressing_ZV0032_opts_in_to_an_experimental_api()
    {
        var source = Model("", """
            #pragma warning disable ZV0032 // opted in to ZX001
                [Must(nameof(Ok))] public string? Code { get; set; }
            #pragma warning restore ZV0032
            """, "[System.Diagnostics.CodeAnalysis.Experimental(\"ZX001\")] public bool Ok(string? value) => true;");

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(WithId(result, "ZV0032"), d => !d.IsSuppressed);
        EmitSucceeds(output);
    }

    [Theory]
    // A model with a nested [Validate] property and a validated collection takes the nested
    // emit path, with and without model-level StopOnFirstFailure. The null test still precedes
    // the predicate in the same property group.
    [InlineData("", "[NotNull][Must(nameof(Ok))] public string Code { get; set; } = \"\";", true)]
    [InlineData("StopOnFirstFailure = true", "[NotNull][Must(nameof(Ok))] public string Code { get; set; } = \"\";", true)]
    // Property-level StopOnFirstFailure chains the rules with else if, so the predicate only
    // runs once the null test has passed.
    [InlineData("", "[StopOnFirstFailure][NotNull][Must(nameof(Ok))] public string? Code { get; set; }", false)]
    [InlineData("StopOnFirstFailure = true", "[StopOnFirstFailure][NotNull][Must(nameof(Ok))] public string? Code { get; set; }", false)]
    public void Nested_and_collection_model_is_probed_as_generated(string validateArguments, string property, bool warns)
    {
        var source = Prelude.Replace("public static class Probe", "internal static class Unused", StringComparison.Ordinal)
            .Replace("new RequestValidator()", "new ItemValidator()", StringComparison.Ordinal)
            .Replace("new Request()", "new Item()", StringComparison.Ordinal) + $$"""
            [Validate]
            public class Address
            {
                [NotEmpty] public string? Street { get; set; }
            }

            [Validate]
            public class Item
            {
                [NotEmpty] public string? Sku { get; set; }
            }

            [Validate({{validateArguments}})]
            public class Request
            {
                {{property}}

                public Address? Home { get; set; }

                public List<Item> Items { get; set; } = new();

                public bool Ok(string value) => true;
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Equal(warns ? 1 : 0, WithId(result, "ZV0032").Length);
        Assert.Equal(warns, GeneratedValidator(result).Contains("#pragma warning disable CS8604", StringComparison.Ordinal));
        Assert.Contains("_homeValidator", GeneratedValidator(result), StringComparison.Ordinal);
        Assert.Contains("_itemsValidator", GeneratedValidator(result), StringComparison.Ordinal);
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

    /// <summary>
    /// Built-in-rule reads, issue #255: a null-check-style built-in rule reads the property twice
    /// in one condition, so both readings are covered by the one site the whole condition gives,
    /// with exactly one pragma around the whole line.
    /// </summary>
    [Fact]
    public void Built_in_rule_condition_that_reads_the_property_twice_gets_one_pragma_for_the_whole_line()
    {
        var source = Model("", "[MinLength(1)][Obsolete] public string? Code { get; set; }", "");

        var (result, output) = RunGenerator(source);

        SingleZV0032(result);
        var generated = GeneratedValidator(result);
        Assert.Contains(
            "#pragma warning disable CS0612 // mirrored as ZV0032 at the attribute this call is made for",
            generated, StringComparison.Ordinal);
        Assert.Contains("instance.Code is not null && (instance.Code.Length < 1)", generated, StringComparison.Ordinal);
        Assert.Equal(1, generated.Split("#pragma warning disable CS").Length - 1);
        Assert.Empty(GeneratedWarnings(result, output));
        FailedProperties(output);
    }

    /// <summary>
    /// Issue #255: an <c>[Obsolete(error: true)]</c> property's read raises CS0619, which, unlike
    /// CS0612 and CS0618, pragma cannot suppress. The rule is left out of the generated file
    /// entirely — not even as "if (false)", which the compiler would flag as CS0162 unreachable
    /// code there — and ZV0032 reports the compiler's own message as an error at the rule's
    /// attribute instead of leaking CS0619 (or CS0162) into the generated file.
    /// </summary>
    [Fact]
    public void Obsolete_error_property_never_reaches_the_generated_file()
    {
        var source = Model("", "[NotEmpty][Obsolete(\"gone\", error: true)] public string? Code { get; set; }", "");

        var (result, output) = RunGenerator(source);

        var zv0032 = SingleZV0032(result);
        Assert.Equal(DiagnosticSeverity.Error, zv0032.Severity);
        Assert.Equal("NotEmpty", SpanText(zv0032));
        Assert.StartsWith(
            "The generated validator's call 'instance.Code', made for [NotEmpty] on 'Code', raises CS0619: ",
            zv0032.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("'gone'", zv0032.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var generated = GeneratedValidator(result);
        Assert.DoesNotContain("instance.Code", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("if (false)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("#pragma", generated, StringComparison.Ordinal);

        // The compilation itself, generated code included, has no CS0619 or CS0162 left anywhere.
        Assert.Empty(GeneratedWarnings(result, output));
        var outputDiagnostics = output.GetDiagnostics().ToList();
        Assert.DoesNotContain(outputDiagnostics, d => string.Equals(d.Id, "CS0619", StringComparison.Ordinal));
        EmitSucceeds(output);

        // The rule never fires, since it is never emitted: no failure for Code.
        Assert.DoesNotContain(FailedProperties(output), p => string.Equals(p, "Code", StringComparison.Ordinal));
    }

    /// <summary>
    /// Issue #255: the same read reported once, by the base type's own validator, when the base
    /// type is also <c>[Validate]</c>.
    /// </summary>
    [Fact]
    public void Obsolete_error_base_usage_is_reported_once_by_the_base_validator()
    {
        var source = Prelude + """
            [Validate]
            public class RequestBase
            {
                [NotEmpty]
                [Obsolete("gone", error: true)]
                public string? Code { get; set; }
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
        EmitSucceeds(output);
    }

    /// <summary>
    /// Issue #265: a <c>[Must]</c> predicate or a custom rule is given the property's value as its
    /// argument, and reading an <c>[Obsolete(error: true)]</c> property there raises CS0619, which
    /// pragma cannot suppress. The rule is left out of the generated file, and ZV0032 reports the
    /// compiler's own message as an error at the rule's attribute. An error-level getter counts
    /// the same as an error-level property.
    /// </summary>
    [Theory]
    [InlineData("[Must(nameof(Ok))][Obsolete(\"gone\", error: true)] public string Code { get; set; } = \"\";",
        "public bool Ok(string value) => true;", "Must(nameof(Ok))", "[Must] on 'Code'", "'Request.Code' is obsolete: 'gone'")]
    [InlineData("[NotBlank][Obsolete(\"gone\", error: true)] public string Code { get; set; } = \"\";",
        "", "NotBlank", "[NotBlank] on 'Code'", "'Request.Code' is obsolete: 'gone'")]
    [InlineData("[Must(nameof(Ok))] public string Code { [Obsolete(\"gone\", error: true)] get; set; } = \"\";",
        "public bool Ok(string value) => true;", "Must(nameof(Ok))", "[Must] on 'Code'", "'Request.Code.get' is obsolete: 'gone'")]
    [InlineData("[NotBlank] public string Code { [Obsolete(\"gone\", error: true)] get; set; } = \"\";",
        "", "NotBlank", "[NotBlank] on 'Code'", "'Request.Code.get' is obsolete: 'gone'")]
    public void Obsolete_error_argument_of_a_predicate_or_custom_rule_never_reaches_the_generated_file(
        string property, string members, string attribute, string usage, string compilerMessage)
    {
        var source = Model("", property, members);

        var (result, output) = RunGenerator(source);

        var zv0032 = SingleZV0032(result);
        Assert.Equal(DiagnosticSeverity.Error, zv0032.Severity);
        Assert.Equal(attribute, SpanText(zv0032));
        Assert.Equal(
            $"The generated validator's call 'instance.Code', made for {usage}, raises CS0619: {compilerMessage}",
            zv0032.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(result.Diagnostics.Length, WithId(result, "ZV0032").Length);

        var generated = GeneratedValidator(result);
        Assert.DoesNotContain("instance.Code", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("#pragma warning disable CS", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratedWarnings(result, output));
        Assert.DoesNotContain(FailedProperties(output), p => string.Equals(p, "Code", StringComparison.Ordinal));
    }

    /// <summary>
    /// Issue #265: a <c>[CustomValidation]</c> method that is itself <c>[Obsolete(error: true)]</c>
    /// used to leak CS0619 into the generated file with no ZeroAlloc diagnostic at all. The probe
    /// compile reports CS0619 on the call, so the call is left out of the generated file rather
    /// than wrapped in a pragma that could not suppress it, and ZV0032 reports the compiler's own
    /// message as an error at the attribute. Every other rule of the model is still emitted and
    /// still runs, and so does a <c>[CustomValidation]</c> method that is not obsolete.
    /// </summary>
    [Theory]
    [InlineData("ValidationFailure[]", "Array.Empty<ValidationFailure>()")]
    [InlineData("IEnumerable<ValidationFailure>", "Array.Empty<ValidationFailure>()")]
    // A span is walked by reference through a hoisted local; that local is left out too.
    [InlineData("ReadOnlySpan<ValidationFailure>", "default")]
    public void Obsolete_error_custom_validation_method_is_left_out_and_reported(string returnType, string body)
    {
        var source = Model("", "[NotEmpty] public string? Code { get; set; }", $$"""
            [Obsolete("gone", error: true)][CustomValidation] public {{returnType}} Check() => {{body}};
                [CustomValidation] public ValidationFailure[] Fine() => new[] { new ValidationFailure { PropertyName = "Fine", ErrorMessage = "fine" } };
            """);

        var (result, output) = RunGenerator(source);

        var zv0032 = SingleZV0032(result);
        Assert.Equal(DiagnosticSeverity.Error, zv0032.Severity);
        Assert.Equal("CustomValidation", SpanText(zv0032));
        Assert.Equal(
            "The generated validator's call 'instance.Check()', made for [CustomValidation] on 'Check', "
            + "raises CS0619: 'Request.Check()' is obsolete: 'gone'",
            zv0032.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(result.Diagnostics.Length, WithId(result, "ZV0032").Length);

        var generated = GeneratedValidator(result);
        Assert.DoesNotContain("Check()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("#pragma warning disable CS", generated, StringComparison.Ordinal);
        Assert.Contains("instance.Fine()", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratedWarnings(result, output));
        Assert.DoesNotContain(output.GetDiagnostics(), d => string.Equals(d.Id, "CS0619", StringComparison.Ordinal));
        Assert.Equal(["Code", "Fine"], FailedProperties(output));
    }

    /// <summary>
    /// Issue #265: an <c>[Obsolete(error: true)]</c> <c>[CustomValidation]</c> method on a
    /// <c>[Validate]</c> base type is left out of both validators, and reported once, by the base
    /// type's own validator.
    /// </summary>
    [Fact]
    public void Obsolete_error_custom_validation_on_a_validated_base_is_reported_once()
    {
        var source = Prelude + """
            [Validate]
            public class RequestBase
            {
                [NotEmpty] public string? Code { get; set; }

                [Obsolete("gone", error: true)]
                [CustomValidation]
                public ValidationFailure[] Check() => Array.Empty<ValidationFailure>();
            }

            [Validate]
            public class Request : RequestBase
            {
                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Equal(DiagnosticSeverity.Error, SingleZV0032(result).Severity);
        Assert.Equal(result.Diagnostics.Length, WithId(result, "ZV0032").Length);
        Assert.DoesNotContain("Check()", GeneratedValidator(result), StringComparison.Ordinal);
        Assert.Empty(GeneratedWarnings(result, output));
        Assert.Equal(["Code", "Other"], FailedProperties(output));
    }

    /// <summary>
    /// Issue #265: a <c>[Must]</c> predicate, <c>When</c> guard or <c>[SkipWhen]</c> method that is
    /// itself <c>[Obsolete(error: true)]</c> fails to compile as a call, which ZV0030 already
    /// reports, and is left out: no CS0619 reaches the generated file, and no ZV0032 repeats it.
    /// </summary>
    [Theory]
    [InlineData("", "[Must(nameof(Ok))] public string? Code { get; set; }", "[Obsolete(\"gone\", error: true)] public bool Ok(string? value) => true;", "Must(nameof(Ok))")]
    [InlineData("", "[NotEmpty(When = nameof(Ok))] public string? Code { get; set; }", "[Obsolete(\"gone\", error: true)] public bool Ok() => true;", "NotEmpty(When = nameof(Ok))")]
    [InlineData("[SkipWhen(nameof(Ok))]", "[NotEmpty] public string? Code { get; set; }", "[Obsolete(\"gone\", error: true)] public bool Ok() => true;", "SkipWhen(nameof(Ok))")]
    public void Obsolete_error_rule_method_is_reported_as_ZV0030_and_left_out(
        string classAttributes, string property, string members, string attribute)
    {
        var source = Model(classAttributes, property, members);

        var (result, output) = RunGenerator(source);

        var reported = WithId(result, "ZV0030");
        Assert.True(reported.Length == 1, "Expected exactly one ZV0030, got: " + string.Join("; ", result.Diagnostics.Select(d => d.ToString())));
        var zv0030 = reported[0];
        Assert.Equal(DiagnosticSeverity.Error, zv0030.Severity);
        Assert.Equal(attribute, SpanText(zv0030));
        Assert.Contains("fails with CS0619: 'Request.Ok", zv0030.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(WithId(result, "ZV0032"));
        Assert.DoesNotContain("instance.Ok", GeneratedValidator(result), StringComparison.Ordinal);
        Assert.Empty(GeneratedWarnings(result, output));
        EmitSucceeds(output);
    }

    /// <summary>
    /// Issue #255: a model with no obsolete member at all takes exactly the path it did before —
    /// no ZV0032, no pragma, no literal <c>false</c> condition.
    /// </summary>
    [Fact]
    public void Model_with_no_obsolete_member_is_unaffected()
    {
        var source = Model("", "[NotEmpty] public string? Code { get; set; }", "");

        var (result, output) = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var generated = GeneratedValidator(result);
        Assert.DoesNotContain("#pragma", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("if (false)", generated, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrEmpty(instance.Code))", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratedWarnings(result, output));
        FailedProperties(output);
    }

    private static string Model(string classAttributes, string property, string members, string baseMembers = "") => Prelude + $$"""
        public class RequestBase
        {
            {{baseMembers}}
        }

        {{classAttributes}}
        [Validate]
        public class Request : RequestBase
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

    /// <summary>
    /// Severities as <c>.editorconfig</c> (per tree) or a global analyzer config would set them.
    /// </summary>
    private sealed class ConfiguredSeverities : SyntaxTreeOptionsProvider
    {
        private readonly string? _perTreeId;
        private readonly ReportDiagnostic _perTree;
        private readonly string? _globalId;
        private readonly ReportDiagnostic _global;

        public ConfiguredSeverities(string? perTreeId = null, ReportDiagnostic perTree = ReportDiagnostic.Default,
            string? globalId = null, ReportDiagnostic global = ReportDiagnostic.Default)
        {
            _perTreeId = perTreeId;
            _perTree = perTree;
            _globalId = globalId;
            _global = global;
        }

        public override GeneratedKind IsGenerated(SyntaxTree tree, System.Threading.CancellationToken cancellationToken) =>
            GeneratedKind.Unknown;

        // Like .editorconfig, per-tree severities match files by path, so a tree without one,
        // such as the generator's probe, gets none.
        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, System.Threading.CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = _perTree;
            return tree.FilePath.Length > 0 && string.Equals(_perTreeId, diagnosticId, StringComparison.Ordinal);
        }

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, System.Threading.CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = _global;
            return string.Equals(_globalId, diagnosticId, StringComparison.Ordinal);
        }
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
            [CSharpSyntaxTree.ParseText(source, path: "Request.cs")],
            TrustedPlatformReferences(),
            options ?? Options());

        var driver = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }
}
