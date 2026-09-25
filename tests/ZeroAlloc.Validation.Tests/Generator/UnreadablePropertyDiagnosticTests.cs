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
/// ZV0027: the generated validator reads each validated property as <c>instance.Prop</c>. A rule
/// on a property that expression cannot read, because the property is static, an indexer, has no
/// getter, or its getter is not accessible from the validator, used to be emitted anyway and
/// surfaced as CS0176, CS0154 or CS0122 inside generated code. The rule is now skipped and
/// reported at the attribute, and the rest of the model is still validated.
/// </summary>
public class UnreadablePropertyDiagnosticTests
{
    private const string Prelude = """
        using System;
        using ZeroAlloc.Validation;
        namespace TestModels;

        public sealed class NotBlankAttribute : ValidationAttribute<string?>
        {
            public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
        }

        public sealed class Coordinate { }

        public sealed class CoordinateValidator : ValidatorFor<Coordinate>
        {
            public override ValidationResult Validate(Coordinate instance) =>
                new ValidationResult(Array.Empty<ValidationFailure>());
        }

        [Validate]
        public sealed class Address
        {
            [NotEmpty] public string? Street { get; set; }
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
    // Built-in rules.
    [InlineData("[NotEmpty] public static string? Code { get; set; }",
        "NotEmptyAttribute", "Code", "NotEmpty", "is static")]
    [InlineData("[NotEmpty] public string? Code { set { } }",
        "NotEmptyAttribute", "Code", "NotEmpty", "has no get accessor")]
    [InlineData("[NotEmpty] public string? Code { private get; set; }",
        "NotEmptyAttribute", "Code", "NotEmpty", "has a get accessor the generated validator cannot access")]
    [InlineData("[NotEmpty] protected string? Code { get; set; }",
        "NotEmptyAttribute", "Code", "NotEmpty", "is not accessible from the generated validator")]
    [InlineData("[NotEmpty] private string? Code { get; set; }",
        "NotEmptyAttribute", "Code", "NotEmpty", "is not accessible from the generated validator")]
    [InlineData("[NotEmpty] private protected string? Code { get; set; }",
        "NotEmptyAttribute", "Code", "NotEmpty", "is not accessible from the generated validator")]
    [InlineData("[NotEmpty] public string? Code { protected get; set; }",
        "NotEmptyAttribute", "Code", "NotEmpty", "has a get accessor the generated validator cannot access")]
    [InlineData("[NotEmpty] public string this[int index] => \"\";",
        "NotEmptyAttribute", "this[]", "NotEmpty", "is an indexer")]
    // [Must].
    [InlineData("[Must(nameof(IsValidCode))] public static string? Code { get; set; } public bool IsValidCode(string? value) => value is not null;",
        "MustAttribute", "Code", "Must(nameof(IsValidCode))", "is static")]
    [InlineData("[Must(nameof(IsValidCode))] public string? Code { set { } } public bool IsValidCode(string? value) => value is not null;",
        "MustAttribute", "Code", "Must(nameof(IsValidCode))", "has no get accessor")]
    // Custom ValidationAttribute<T> rules.
    [InlineData("[NotBlank] public static string? Code { get; set; }",
        "NotBlankAttribute", "Code", "NotBlank", "is static")]
    [InlineData("[NotBlank] public string? Code { set { } }",
        "NotBlankAttribute", "Code", "NotBlank", "has no get accessor")]
    [InlineData("[NotBlank] public string? Code { private get; set; }",
        "NotBlankAttribute", "Code", "NotBlank", "has a get accessor the generated validator cannot access")]
    [InlineData("[NotBlank] public string this[int index] => \"\";",
        "NotBlankAttribute", "this[]", "NotBlank", "is an indexer")]
    // Nested validation through [ValidateWith].
    [InlineData("[ValidateWith(typeof(CoordinateValidator))] public static Coordinate? Home { get; set; }",
        "ValidateWithAttribute", "Home", "ValidateWith(typeof(CoordinateValidator))", "is static")]
    [InlineData("[ValidateWith(typeof(CoordinateValidator))] public Coordinate? Home { set { } }",
        "ValidateWithAttribute", "Home", "ValidateWith(typeof(CoordinateValidator))", "has no get accessor")]
    [InlineData("[ValidateWith(typeof(CoordinateValidator))] private Coordinate? Home { get; set; }",
        "ValidateWithAttribute", "Home", "ValidateWith(typeof(CoordinateValidator))", "is not accessible from the generated validator")]
    public void Rule_on_unreadable_property_reports_ZV0027_and_validates_the_rest(
        string member, string attributeName, string propertyName, string spanText, string reason)
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

        var zv0027 = SingleZV0027(result);
        Assert.Equal(DiagnosticSeverity.Error, zv0027.Severity);
        Assert.Equal(spanText, SpanText(zv0027));
        Assert.Equal(
            $"'{attributeName}' is applied to '{propertyName}', which the generated validator cannot read because the property {reason}",
            zv0027.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));

        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("protected internal")]
    public void Rule_on_own_property_readable_from_the_assembly_is_validated(string accessibility)
    {
        // Control: the validator lives in the same assembly, so an internal getter is readable.
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                [NotEmpty] {{accessibility}} string? Code { get; set; }

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Each_rule_on_an_unreadable_property_is_reported()
    {
        var source = Prelude + """
            [Validate]
            public class Request
            {
                [NotEmpty, NotBlank] public static string? Code { get; set; }

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Equal(2, result.Diagnostics.Count(d => string.Equals(d.Id, "ZV0027", StringComparison.Ordinal)));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("public static Address? Home { get; set; }")]
    [InlineData("public Address? Home { set { } }")]
    [InlineData("private Address? Home { get; set; }")]
    [InlineData("public Address? Home { private get; set; }")]
    [InlineData("public static System.Collections.Generic.List<Address>? Homes { get; set; }")]
    [InlineData("public System.Collections.Generic.List<Address>? Homes { set { } }")]
    public void Unreadable_property_of_a_Validate_type_is_not_composed_and_not_reported(string member)
    {
        // No attribute asked for this property to be validated: nested composition is implicit,
        // so a property the validator cannot read is simply not part of what it validates.
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                {{member}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("[NotEmpty] public static string? Code { get; set; }", "is static")]
    [InlineData("[NotEmpty] public string? Code { set { } }", "has no get accessor")]
    public void Rule_on_unreadable_base_property_reports_ZV0027_not_ZV0017(string member, string reason)
    {
        var source = Prelude + $$"""
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

        var zv0027 = SingleZV0027(result);
        Assert.EndsWith(reason, zv0027.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Rule_on_inaccessible_base_property_stays_ZV0017()
    {
        // An inherited member the validator cannot reach is ZV0017's case, a warning: the base
        // type may belong to someone else. ZV0027 is not reported on top of it.
        var source = Prelude + """
            public class RequestBase
            {
                [NotEmpty] protected string? Code { get; set; }
            }

            [Validate]
            public class Request : RequestBase
            {
                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0027", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Static_property_hiding_a_readable_base_property_is_not_validated()
    {
        // `instance.Code` binds to the derived static property, so the base rule cannot be read
        // through it either: the hidden base property must not resurface in the validator.
        var source = Prelude + """
            public class RequestBase
            {
                [NotEmpty] public string? Code { get; set; }
            }

            [Validate]
            public class Request : RequestBase
            {
                [NotEmpty] public static new string? Code { get; set; }

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleZV0027(result);
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    private static Diagnostic SingleZV0027(GeneratorDriverRunResult result)
    {
        var matches = result.Diagnostics
            .Where(d => string.Equals(d.Id, "ZV0027", StringComparison.Ordinal))
            .ToArray();
        Assert.True(matches.Length == 1, "Expected exactly one ZV0027, got: "
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

    private static (GeneratorDriverRunResult Result, Compilation Output) RunGenerator(string source)
    {
        var trusted = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "UnreadablePropertyTests_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }
}
