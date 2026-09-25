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
/// Guards issue #219. The generator has no support for a generic <c>[Validate]</c> model, or for
/// one nested in a generic type: it emitted a validator that named the model without its type
/// parameters, so the user got compiler errors inside generated code and none at their own type.
/// ZV0029 now reports the attribute, and no generator emits code that names the type.
/// </summary>
public class GenericValidateTypeDiagnosticTests
{
    private const string Rule = """[NotEmpty] public string Name { get; set; } = "";""";

    /// <summary>Each source declares one generic [Validate] type, and the name ZV0029 gives it.</summary>
    public static TheoryData<string, string> GenericTypes() => new()
    {
        { $$"""[Validate] public class Inner<T> { {{Rule}} public T? Value { get; set; } }""", "MyApp.Inner<T>" },
        { $$"""[Validate] public record Inner<TKey, TValue> { {{Rule}} }""", "MyApp.Inner<TKey, TValue>" },
        { $$"""public class Outer<T> { [Validate] public class Inner { {{Rule}} } }""", "MyApp.Outer<T>.Inner" },
        { $$"""public class Outer<T> { public class Mid { [Validate] internal class Inner { {{Rule}} } } }""", "MyApp.Outer<T>.Mid.Inner" },
        { $$"""public class Outer<T> { [Validate] public class Inner<U> { {{Rule}} } }""", "MyApp.Outer<T>.Inner<U>" },
    };

    [Theory]
    [MemberData(nameof(GenericTypes))]
    public void GenericValidateType_ReportsZV0029AtTheAttribute(string declaration, string displayName)
    {
        var (compilation, diagnostics, generated) = Run(Source(declaration), new ValidatorGenerator());

        Assert.Equal(["ZV0029"], diagnostics.ConvertAll(d => d.Id));
        var zv0029 = diagnostics[0];
        Assert.Equal(DiagnosticSeverity.Error, zv0029.Severity);
        Assert.Equal("Validate", SourceAt(zv0029));
        Assert.Equal(
            $"'{displayName}' is generic or declared inside a generic type, so no validator is generated for it; "
                + "validate a non-generic type instead",
            zv0029.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(generated, s => s.Contains("InnerValidator", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
    }

    [Theory]
    [MemberData(nameof(GenericTypes))]
    public void GenericValidateType_IsLeftOutOfTheInjectAndOptionsGlue(string declaration, string displayName)
    {
        _ = displayName;
        var source = Source(declaration) + """

            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }

            internal static class Wiring
            {
                public static void Wire(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions.ValidateWithZeroAlloc(
                        Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.AddOptions<Customer>(services));
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
        Assert.Contains(generated, s => s.Contains("global::MyApp.CustomerValidator>", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, s => s.Contains("Inner", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(GenericTypes))]
    public void GenericValidateType_IsLeftOutOfTheAspNetCoreGlue(string declaration, string displayName)
    {
        // This host does not reference ASP.NET Core, so only the emitted text is checked; the
        // glue is compiled and run for real in ZeroAlloc.Validation.Tests.AspNetCore.
        _ = displayName;
        var source = Source(declaration) + """

            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var (_, _, generated) = Run(source, new global::ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter());

        Assert.Contains(generated, s => s.Contains("global::MyApp.CustomerValidator>", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, s => s.Contains("Inner", StringComparison.Ordinal));
    }

    [Fact]
    public void PropertyOfGenericValidateType_IsNotWiredToAValidatorThatIsNeverGenerated()
    {
        // Order is not generic and gets a validator. Its properties use closed forms of Inner and
        // Outer<T>.Line, which get none, so Order's validator must not take one as a dependency.
        var source = Source($$"""
            public class Outer<T> { [Validate] public class Line { {{Rule}} } }

            [Validate] public class Inner<T> { {{Rule}} }

            [Validate]
            public class Order
            {
                [NotEmpty] public string Id { get; set; } = "";
                public Inner<int>? First { get; set; }
                public System.Collections.Generic.List<Inner<string>> Items { get; set; } = new();
                public Outer<int>.Line? Line { get; set; }
            }
            """);

        var (compilation, diagnostics, generated) = Run(
            source,
            new ValidatorGenerator(),
            new global::ZeroAlloc.Validation.Inject.InjectGenerator());

        Assert.Equal(["ZV0029", "ZV0029"], diagnostics.ConvertAll(d => d.Id));
        Assert.Empty(Errors(compilation));
        Assert.DoesNotContain(generated, s => s.Contains("InnerValidator", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, s => s.Contains("LineValidator", StringComparison.Ordinal));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.OrderValidator"));
    }

    [Fact]
    public void GenericTypeTheValidatorCannotReach_ReportsBothErrors()
    {
        // Each error names a change the type needs; reporting only one would hide the other
        // until the first is fixed.
        var source = Source($$"""public class Outer<T> { [Validate] private class Inner { {{Rule}} } }""");

        var (compilation, diagnostics, generated) = Run(source, new ValidatorGenerator());

        var ids = diagnostics.ConvertAll(d => d.Id);
        ids.Sort(StringComparer.Ordinal);
        Assert.Equal(["ZV0025", "ZV0029"], ids);
        Assert.DoesNotContain(generated, s => s.Contains("InnerValidator", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
    }

    [Fact]
    public void ModelDerivedFromGenericValidateBase_ReportsTheBaseMembers()
    {
        // A [Validate] base type normally reports its own members, so a derived model leaves them
        // alone. A generic base gets no validator, so nothing would report them: the derived
        // model, whose validator does run the inherited rules, reports them instead.
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace MyApp;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public class Base<T>
            {
                [NotBlank] public string? Code;

                [Must(nameof(IsKnown))] public string Name { get; set; } = "";

                [NotEmpty] protected string? Secret { get; set; }

                [NotEmpty] public static string? Shared { get; set; }

                public static bool IsKnown(string value) => value.Length > 0;
            }

            [Validate]
            public class Derived : Base<int> { }
            """;

        var (compilation, diagnostics, _) = Run(source, new ValidatorGenerator());

        // ZV0024 for the field Code, ZV0028 for the static IsKnown, ZV0017 for the protected
        // Secret and ZV0027 for the static Shared: all reported by Derived. ZV0029 for Base.
        var ids = diagnostics.ConvertAll(d => d.Id);
        ids.Sort(StringComparer.Ordinal);
        Assert.Equal(["ZV0017", "ZV0024", "ZV0027", "ZV0028", "ZV0029"], ids);
        Assert.Empty(Errors(compilation));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.DerivedValidator"));
    }

    [Fact]
    public void ModelDerivedFromGenericBase_GetsAValidatorForTheInheritedRules()
    {
        // The fix ZV0029 documents: rules on a generic base type, [Validate] on a non-generic
        // model that derives from it.
        var source = Source("""
            public class Page<T> { [NotEmpty] public string Title { get; set; } = ""; public T? Item { get; set; } }

            public class Order { }

            [Validate] public class OrderPage : Page<Order> { }
            """);

        var (compilation, diagnostics, generated) = Run(source, new ValidatorGenerator());

        Assert.Empty(diagnostics);
        Assert.Empty(Errors(compilation));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.OrderPageValidator"));
        Assert.Contains(generated, s => s.Contains("\"Title\"", StringComparison.Ordinal));
    }

    [Fact]
    public void MembersOfAGenericValidateBase_AreReportedOnce_ByTheNearestValidatedModel()
    {
        // Derived gets a validator and reports Base<int>'s members. Derived2 derives from
        // Derived, a [Validate] base with a validator, so it leaves them to Derived.
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace MyApp;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public class Base<T>
            {
                [NotBlank] public string? Code;

                [Must(nameof(IsKnown))] public string Name { get; set; } = "";

                [NotEmpty] protected string? Secret { get; set; }

                [NotEmpty] public static string? Shared { get; set; }

                public static bool IsKnown(string value) => value.Length > 0;
            }

            [Validate]
            public class Derived : Base<int> { }

            [Validate]
            public class Derived2 : Derived { [NotEmpty] public string Extra { get; set; } = ""; }
            """;

        var (compilation, diagnostics, _) = Run(source, new ValidatorGenerator());

        var ids = diagnostics.ConvertAll(d => d.Id);
        ids.Sort(StringComparer.Ordinal);
        Assert.Equal(["ZV0017", "ZV0024", "ZV0027", "ZV0028", "ZV0029"], ids);
        Assert.Empty(Errors(compilation));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.DerivedValidator"));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.Derived2Validator"));
    }

    [Fact]
    public void ValidateWithOnAPropertyOfGenericValidateType_IsNotReportedAsRedundant()
    {
        // ZV0011 tells the user to drop [ValidateWith] and use the auto-generated validator.
        // Box<int> has none, so [ValidateWith] is the way to validate it and must stay.
        var source = Source($$"""
            [Validate] public class Box<T> { {{Rule}} }

            public class BoxOfIntValidator : ValidatorFor<Box<int>>
            {
                public override ValidationResult Validate(Box<int> instance) =>
                    new(new ValidationFailure[0]);
            }

            [Validate]
            public class Order
            {
                [ValidateWith(typeof(BoxOfIntValidator))]
                public Box<int> Item { get; set; } = new();
            }
            """);

        var (compilation, diagnostics, _) = Run(source, new ValidatorGenerator());

        Assert.Equal(["ZV0029"], diagnostics.ConvertAll(d => d.Id));
        Assert.Empty(Errors(compilation));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.OrderValidator"));
    }

    private static string Source(string declaration) => $$"""
        using ZeroAlloc.Validation;
        namespace MyApp;

        {{declaration}}
        """;

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
                errors.Add($"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
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
