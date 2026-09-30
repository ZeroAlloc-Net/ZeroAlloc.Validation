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
/// Guards issues #219 and #238. A generic <c>[Validate]</c> model gets a validator generic over
/// the type parameters of the model and of every type containing it, with their declared names;
/// see <see cref="GenericValidateModelTests"/>. ZV0029 remains for the type parameters that
/// validator cannot declare: one whose name repeats along the containing chain, one named like the
/// validator itself, or one named like a member the validator declares or reserves, #299. It
/// reports the attribute, and no generator emits code that names the type.
/// </summary>
public class GenericValidateTypeDiagnosticTests
{
    private const string Rule = """[NotEmpty] public string Name { get; set; } = "";""";

    private const string Repeated = "more than once along its containing types";
    private const string MemberName = "with a name the generated validator declares or reserves for its members";

    /// <summary>
    /// Each source declares one [Validate] type ZV0029 reports, the name it gives it, the type
    /// parameter it names, and why the validator cannot declare that type parameter.
    /// </summary>
    public static TheoryData<string, string, string, string> UnsupportedTypes() => new()
    {
        { $$"""public class Outer<T> { [Validate] public class Inner<T> { {{Rule}} } }""", "MyApp.Outer<T>.Inner<T>", "T", Repeated },
        { $$"""public class Outer<T> { public class Mid { [Validate] internal class Inner<T> { {{Rule}} } } }""", "MyApp.Outer<T>.Mid.Inner<T>", "T", Repeated },
        { $$"""public class Outer<T, U> { [Validate] public class Inner<V, U> { {{Rule}} } }""", "MyApp.Outer<T, U>.Inner<V, U>", "U", Repeated },
        { $$"""[Validate] public class Box<BoxValidator> { {{Rule}} }""", "MyApp.Box<BoxValidator>", "BoxValidator", "with the name of its generated validator 'BoxValidator'" },
        { $$"""public class Outer<T> { [Validate] public class Inner<Outer_InnerValidator> { {{Rule}} } }""", "MyApp.Outer<T>.Inner<Outer_InnerValidator>", "Outer_InnerValidator", "with the name of its generated validator 'Outer_InnerValidator'" },
        // A type parameter shares the member declaration space of the validator, CS0102, #299.
        { $$"""[Validate] public class Box<Validate> { {{Rule}} }""", "MyApp.Box<Validate>", "Validate", MemberName },
        { $$"""[Validate] public class Box<ValidateAsync> { {{Rule}} }""", "MyApp.Box<ValidateAsync>", "ValidateAsync", MemberName },
        { $$"""public class Outer<ValidateAsync> { [Validate] public class Inner { {{Rule}} } }""", "MyApp.Outer<ValidateAsync>.Inner", "ValidateAsync", MemberName },
        { $$"""[Validate] public class Box<__Regex_Name> { {{Rule}} }""", "MyApp.Box<__Regex_Name>", "__Regex_Name", MemberName },
        { $$"""[Validate] public class Box<__Rule_Name_0> { {{Rule}} }""", "MyApp.Box<__Rule_Name_0>", "__Rule_Name_0", MemberName },
        { $$"""[Validate] public class Box<__ValidateAsyncCore> { {{Rule}} }""", "MyApp.Box<__ValidateAsyncCore>", "__ValidateAsyncCore", MemberName },
        { $$"""[Validate] public class Box<_nameValidator> { {{Rule}} }""", "MyApp.Box<_nameValidator>", "_nameValidator", MemberName },
    };

    [Theory]
    [MemberData(nameof(UnsupportedTypes))]
    public void TypeParameterTheValidatorCannotDeclare_ReportsZV0029AtTheAttribute(string declaration, string displayName, string parameter, string reason)
    {
        var (compilation, diagnostics, generated) = Run(Source(declaration), new ValidatorGenerator());

        Assert.Equal(["ZV0029"], diagnostics.ConvertAll(d => d.Id));
        var zv0029 = diagnostics[0];
        Assert.Equal(DiagnosticSeverity.Error, zv0029.Severity);
        Assert.Equal("Validate", SourceAt(zv0029));
        Assert.Equal(
            $"'{displayName}' declares type parameter '{parameter}' {reason}, so no validator is generated; rename it",
            zv0029.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(generated, s => s.Contains("Validator :", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
    }

    /// <summary>
    /// A type parameter named like something the validator declares only as a parameter or a
    /// local does not clash with it, and neither does a name that merely resembles a member's.
    /// </summary>
    [Theory]
    [InlineData("instance")]
    [InlineData("ct")]
    [InlineData("nameValidator")]
    [InlineData("Validator")]
    [InlineData("TValidate")]
    [InlineData("_Name")]
    [InlineData("T__Rule")]
    public void TypeParameterNamedLikeNoValidatorMember_GetsAValidator(string parameter)
    {
        var source = Source($$"""
            [Validate] public class Box<{{parameter}}> { {{Rule}} public {{parameter}}? Payload { get; set; } }
            """);

        var (compilation, diagnostics, generated) = Run(source, new ValidatorGenerator());

        Assert.Empty(diagnostics);
        Assert.Contains(generated, s => s.Contains($"class BoxValidator<{parameter}>", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
    }

    [Theory]
    [MemberData(nameof(UnsupportedTypes))]
    public void TypeParameterTheValidatorCannotDeclare_IsLeftOutOfTheInjectAndOptionsGlue(string declaration, string displayName, string parameter, string reason)
    {
        _ = displayName;
        _ = parameter;
        _ = reason;
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
        Assert.DoesNotContain(generated, s => s.Contains("Inner", StringComparison.Ordinal) || s.Contains("Box", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(UnsupportedTypes))]
    public void TypeParameterTheValidatorCannotDeclare_IsLeftOutOfTheAspNetCoreGlue(string declaration, string displayName, string parameter, string reason)
    {
        // This host does not reference ASP.NET Core, so only the emitted text is checked; the
        // glue is compiled and run for real in ZeroAlloc.Validation.Tests.AspNetCore.
        _ = displayName;
        _ = parameter;
        _ = reason;
        var source = Source(declaration) + """

            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var (_, _, generated) = Run(source, new global::ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter());

        Assert.Contains(generated, s => s.Contains("global::MyApp.CustomerValidator>", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, s => s.Contains("Inner", StringComparison.Ordinal) || s.Contains("Box", StringComparison.Ordinal));
    }

    [Fact]
    public void PropertyOfATypeWithoutAValidator_IsNotWiredToAValidatorThatIsNeverGenerated()
    {
        // Order gets a validator. Its properties use closed forms of types ZV0029 reports, which
        // get none, so Order's validator must not take one as a dependency.
        var source = Source($$"""
            public class Outer<T> { [Validate] public class Line<T> { {{Rule}} } }

            [Validate] public class Box<BoxValidator> { {{Rule}} }

            [Validate]
            public class Order
            {
                [NotEmpty] public string Id { get; set; } = "";
                public Box<int>? First { get; set; }
                public System.Collections.Generic.List<Box<string>> Items { get; set; } = new();
                public Outer<int>.Line<string>? Line { get; set; }
            }
            """);

        var (compilation, diagnostics, generated) = Run(
            source,
            new ValidatorGenerator(),
            new global::ZeroAlloc.Validation.Inject.InjectGenerator());

        Assert.Equal(["ZV0029", "ZV0029"], diagnostics.ConvertAll(d => d.Id));
        Assert.Empty(Errors(compilation));
        Assert.DoesNotContain(generated, s => s.Contains("BoxValidator<", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, s => s.Contains("LineValidator", StringComparison.Ordinal));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.OrderValidator"));
        Assert.Null(compilation.GetTypeByMetadataName("MyApp.OrderValidator")!.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length > 0));
    }

    [Fact]
    public void TypeTheValidatorCannotReachOrDeclare_ReportsBothErrors()
    {
        // Each error names a change the type needs; reporting only one would hide the other
        // until the first is fixed.
        var source = Source($$"""public class Outer<T> { [Validate] private class Inner<T> { {{Rule}} } }""");

        var (compilation, diagnostics, generated) = Run(source, new ValidatorGenerator());

        var ids = diagnostics.ConvertAll(d => d.Id);
        ids.Sort(StringComparer.Ordinal);
        Assert.Equal(["ZV0025", "ZV0029"], ids);
        Assert.DoesNotContain(generated, s => s.Contains("InnerValidator", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
    }

    [Fact]
    public void ModelDerivedFromAValidateBaseWithoutAValidator_ReportsTheBaseMembers()
    {
        // A [Validate] base type normally reports its own members, so a derived model leaves them
        // alone. A base ZV0029 reports gets no validator, so nothing would report them: the
        // derived model, whose validator does run the inherited rules, reports them instead.
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace MyApp;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            public class Outer<T>
            {
                [Validate]
                public class Base<T>
                {
                    [NotBlank] public string? Code;

                    [Must(nameof(IsKnown))] public string Name { get; set; } = "";

                    [NotEmpty] protected string? Secret { get; set; }

                    [NotEmpty] public static string? Shared { get; set; }

                    public static bool IsKnown(string value) => value.Length > 0;
                }
            }

            [Validate]
            public class Derived : Outer<int>.Base<int> { }

            [Validate]
            public class Derived2 : Derived { [NotEmpty] public string Extra { get; set; } = ""; }
            """;

        var (compilation, diagnostics, _) = Run(source, new ValidatorGenerator());

        // ZV0024 for the field Code, ZV0028 for the static IsKnown, ZV0017 for the protected
        // Secret and ZV0027 for the static Shared: all reported by Derived, not again by Derived2,
        // which leaves them to its [Validate] base Derived. ZV0029 for Base.
        var ids = diagnostics.ConvertAll(d => d.Id);
        ids.Sort(StringComparer.Ordinal);
        Assert.Equal(["ZV0017", "ZV0024", "ZV0027", "ZV0028", "ZV0029"], ids);
        Assert.Empty(Errors(compilation));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.DerivedValidator"));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.Derived2Validator"));
    }

    [Fact]
    public void ModelDerivedFromGenericBase_GetsAValidatorForTheInheritedRules()
    {
        // Rules on a generic base type that is not a model, [Validate] on a non-generic model
        // that derives from it.
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
    public void ValidateWithOnAPropertyOfATypeWithoutAValidator_IsNotReportedAsRedundant()
    {
        // ZV0011 tells the user to drop [ValidateWith] and use the auto-generated validator.
        // Box<int> has none, so [ValidateWith] is the way to validate it and must stay.
        var source = Source($$"""
            [Validate] public class Box<BoxValidator> { {{Rule}} }

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
