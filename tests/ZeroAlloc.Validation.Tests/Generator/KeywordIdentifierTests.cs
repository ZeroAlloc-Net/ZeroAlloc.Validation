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
/// Guards issue #242. A member declared with a keyword name, such as <c>@class</c>, has the
/// symbol name <c>class</c>. The generator emitted that name as it was, so
/// <c>instance.class</c> did not parse and every rule on the property broke the generated
/// validator. Every identifier the generators emit is now escaped where it is a keyword. The
/// runtime counterpart is <c>Integration/KeywordNamedModelTests</c>.
/// </summary>
public class KeywordIdentifierTests
{
    /// <summary>
    /// Reserved keywords, which need the <c>@</c> escape, and contextual keywords, which are
    /// ordinary identifiers after <c>instance.</c> but must keep working too.
    /// </summary>
    public static TheoryData<string> Keywords() => new()
    {
        "class", "event", "string", "int", "namespace", "operator", "this", "base", "return", "default",
        "var", "value", "await", "nameof", "field", "record", "async", "dynamic", "global", "when",
    };

    [Theory]
    [MemberData(nameof(Keywords))]
    public void KeywordProperty_EveryRuleKind_Compiles(string keyword)
    {
        var source = EveryRuleKindSource(keyword);

        var (compilation, diagnostics, generated) = Run(source, new ValidatorGenerator());

        Assert.Empty(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(Describe));
        Assert.Empty(Errors(compilation));
        Assert.Equal(14, generated.Count);
        Assert.Contains(generated, s => s.Contains($"PropertyName = \"{keyword}\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("class")]
    [InlineData("event")]
    public void KeywordProperty_IsEscapedInEveryAccess(string keyword)
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace MyApp;

            [Validate]
            public class Child { [NotEmpty] public string? Name { get; set; } }

            [Validate]
            public class Model
            {
                [NotEmpty(Message = "{PropertyValue}")] public string? @{{keyword}} { get; set; }
                public Child? Inner { get; set; }
            }
            """;

        var (compilation, _, generated) = Run(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        var validator = generated.Find(s => s.Contains("class ModelValidator", StringComparison.Ordinal));
        Assert.NotNull(validator);
        Assert.Contains($"instance.@{keyword}", validator, StringComparison.Ordinal);
        Assert.DoesNotContain($"instance.{keyword}", validator, StringComparison.Ordinal);
        Assert.Contains($"PropertyName = \"{keyword}\"", validator, StringComparison.Ordinal);
    }

    [Fact]
    public void KeywordMethodName_IsCalled_NotReportedAsNoMethodName()
    {
        // A When, Unless, [Must], [SkipWhen] or [CustomValidation] method declared as @class was
        // reported as ZV0030 "'class' is not a method name", although instance.@class() binds.
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace MyApp;

            [Validate]
            public class Model
            {
                [NotEmpty(When = nameof(@class))] public string? Name { get; set; }
                public bool @class() => true;

                [CustomValidation]
                public IEnumerable<ValidationFailure> @event() { yield break; }
            }
            """;

        var (compilation, diagnostics, generated) = Run(source, new ValidatorGenerator());

        Assert.Empty(diagnostics.Select(Describe));
        Assert.Empty(Errors(compilation));
        var validator = generated.Find(s => s.Contains("class ModelValidator", StringComparison.Ordinal));
        Assert.NotNull(validator);
        Assert.Contains("instance.@class() && ", validator, StringComparison.Ordinal);
        Assert.Contains("instance.@event()", validator, StringComparison.Ordinal);
    }

    private const string KeywordNamespaceSource = """
        using ZeroAlloc.Validation;

        namespace @class.@event
        {
            [Validate]
            public class @record { [NotEmpty] public string? @string { get; set; } }

            public class @operator
            {
                [Validate] public class @int { [NotEmpty] public string? Name { get; set; } }
            }

            [Validate]
            public class Holder
            {
                public @record? @default { get; set; }
                public System.Collections.Generic.List<@operator.@int> @this { get; set; } = new();
            }
        }
        """;

    [Fact]
    public void KeywordNamespaceAndTypeNames_ValidatorsCompile()
    {
        var (compilation, diagnostics, generated) = Run(KeywordNamespaceSource, new ValidatorGenerator());

        Assert.Empty(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(Describe));
        Assert.Empty(Errors(compilation));
        Assert.Equal(3, generated.Count);
        Assert.Contains(generated, s => s.Contains("namespace @class.@event;", StringComparison.Ordinal));
        Assert.NotNull(compilation.GetTypeByMetadataName("class.event.recordValidator"));
        Assert.NotNull(compilation.GetTypeByMetadataName("class.event.operator_intValidator"));
    }

    [Fact]
    public void KeywordNamespaceAndTypeNames_InjectAndOptionsGlueCompiles()
    {
        var source = KeywordNamespaceSource + """

            internal static class Wiring
            {
                public static void Wire(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions.ValidateWithZeroAlloc(
                        Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions
                            .AddOptions<@class.@event.@record>(services));
                    services.AddZeroAllocValidators();
                }
            }
            """;

        var (compilation, _, generated) = Run(
            source,
            new ValidatorGenerator(),
            new OptionsValidationEmitter(),
            new global::ZeroAlloc.Validation.Inject.InjectGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Contains(generated, s => s.Contains(
            "TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::@class.@event.@operator.@int>, global::@class.@event.operator_intValidator>",
            StringComparison.Ordinal));
        Assert.Contains(generated, s => s.Contains(
            "OptionsBuilder<global::@class.@event.@record> ValidateWithZeroAlloc(",
            StringComparison.Ordinal));
    }

    [Fact]
    public void KeywordNamespaceAndTypeNames_AspNetCoreGlueIsEscaped()
    {
        // This host does not reference ASP.NET Core, so the glue is parsed and its text checked;
        // it is compiled and run for real in ZeroAlloc.Validation.Tests.AspNetCore.
        var (_, _, generated) = Run(
            KeywordNamespaceSource,
            new global::ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter());

        Assert.Equal(2, generated.Count);
        Assert.Empty(SyntaxErrors(generated));
        Assert.Contains(generated, s => s.Contains(
            "TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::@class.@event.@record>, global::@class.@event.recordValidator>",
            StringComparison.Ordinal));
        Assert.Contains(generated, s => s.Contains("case global::@class.@event.@operator.@int ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Fourteen models, each using <c>@KEYWORD</c> as a property or method name: every
    /// built-in rule, a custom rule, <c>[Must]</c>, <c>When</c>, <c>Unless</c>, <c>[SkipWhen]</c>,
    /// <c>[CustomValidation]</c>, a nested model, a collection, model-level fail-fast, the
    /// <c>{PropertyValue}</c> placeholder and a value object unwrapped through the keyword.
    /// </summary>
    // Methods are named by string: in a type that declares a method named nameof, nameof(...)
    // is a call to it.
    private const string EveryRuleKindTemplate = """
        using System;
        using System.Collections.Generic;
        using ZeroAlloc.Validation;

        namespace ZeroAlloc.ValueObjects
        {
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
            public sealed class ValueObjectAttribute : Attribute { }
        }

        namespace MyApp
        {
            public enum Color { Red, Green }

            [AttributeUsage(AttributeTargets.Property)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly struct Code
            {
                public Code(string v) { @KEYWORD = v; }
                public string @KEYWORD { get; }
            }

            [Validate]
            public class Child { [NotEmpty] public string? Name { get; set; } }

            [Validate]
            public class Text
            {
                [NotNull, NotEmpty, MinLength(1), MaxLength(9), Length(1, 9), EmailAddress, Matches("^a"),
                 Equal("a"), NotEqual("b"), IsEnumName(typeof(Color)), NotBlank]
                [NotEmpty(Message = "{PropertyName} is '{PropertyValue}'")]
                public string? @KEYWORD { get; set; }
            }

            [Validate]
            public class Numbers
            {
                [GreaterThan(0), LessThan(9), GreaterThanOrEqualTo(0), LessThanOrEqualTo(9),
                 InclusiveBetween(0, 9), ExclusiveBetween(0, 9), Equal(1), NotEqual(2)]
                [GreaterThan(0, Message = "{PropertyValue}")]
                public int @KEYWORD { get; set; }
            }

            [Validate]
            public class Amounts
            {
                [PrecisionScale(5, 2)] public decimal @KEYWORD { get; set; }
            }

            [Validate]
            public class Tints { [IsInEnum] public Color? @KEYWORD { get; set; } }

            [Validate]
            public class Empties { [Null, Empty] public string? @KEYWORD { get; set; } }

            [Validate]
            public class Wrapped { [NotEmpty(Message = "{PropertyValue}")] public Code Id { get; set; } }

            [Validate]
            public class Predicate
            {
                [Must(nameof(IsKnown))] public string? @KEYWORD { get; set; }
                public bool IsKnown(string? v) => v is not null;
            }

            [Validate]
            public class Nested { public Child? @KEYWORD { get; set; } }

            [Validate]
            public class Many { public List<Child> @KEYWORD { get; set; } = new(); }

            [Validate(StopOnFirstFailure = true)]
            public class Stopping
            {
                [NotEmpty, MaxLength(3)] public string? @KEYWORD { get; set; }
                public Child? Inner { get; set; }
            }

            [Validate]
            [SkipWhen("KEYWORD")]
            public class Guarded
            {
                [NotEmpty(When = "KEYWORD")] public string? A { get; set; }
                [NotEmpty(Unless = "KEYWORD")] public string? B { get; set; }
                public bool @KEYWORD() => true;
            }

            [Validate]
            public class Checked
            {
                [Must("KEYWORD")] public string? Name { get; set; }
                public bool @KEYWORD(string? v) => v is not null;
            }

            [Validate]
            public class Custom
            {
                [NotEmpty] public string? Name { get; set; }

                [CustomValidation]
                public IEnumerable<ValidationFailure> @KEYWORD() { yield break; }
            }
        }
        """;

    private static string EveryRuleKindSource(string keyword) =>
        EveryRuleKindTemplate.Replace("KEYWORD", keyword, StringComparison.Ordinal);

    private static List<string> SyntaxErrors(List<string> files)
    {
        var errors = new List<string>();
        for (var i = 0; i < files.Count; i++)
        {
            foreach (var d in CSharpSyntaxTree.ParseText(files[i]).GetDiagnostics())
                errors.Add(d.ToString());
        }
        return errors;
    }

    private static string Describe(Diagnostic d) =>
        $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}";

    private static List<string> Errors(Compilation compilation)
    {
        var errors = new List<string>();
        foreach (var d in compilation.GetDiagnostics())
        {
            // Id and message only: the default formatting leads with the generated file path.
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add(Describe(d));
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
