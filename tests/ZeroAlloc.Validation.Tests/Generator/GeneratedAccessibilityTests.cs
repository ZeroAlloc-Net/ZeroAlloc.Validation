using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using ZeroAlloc.Validation.Generator;
using ZeroAlloc.Validation.Options.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Guards issue #184: generated types follow the effective accessibility of the model.
/// A validator was always emitted public, so an internal [Validate] type failed with CS9338
/// and CS0051, and the options extension class exposed internal models from a public type.
///
/// Also guards points 1 and 3 of issue #193. Point 1: the opt-in ZeroAllocGeneratedAccessibility
/// MSBuild property makes every generated entry point internal, even for a public model. Point
/// 3 extends the #184 rule itself, unconditionally: a validator is public only if the model is
/// effectively public AND every nested [Validate] model it takes as a constructor-injected
/// dependency would itself resolve to a public validator, computed transitively — so a public
/// model with an internal nested [Validate] model now compiles (and is internal) even with the
/// property unset, no opt-in required.
/// </summary>
public class GeneratedAccessibilityTests
{
    [Fact]
    public void InternalModel_ValidatorCompiles_AndIsInternal()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.JevOptionsValidator"));
    }

    [Fact]
    public void InternalRecord_ValidatorCompiles_AndIsInternal()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal sealed record Request([property: NotEmpty] string Name);
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.RequestValidator"));
    }

    [Fact]
    public void InternalModelWithInternalNestedModel_Compiles()
    {
        // The nested model's validator is a constructor parameter of the outer one, so both
        // must end up with an accessibility the other can use.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal class Address { [NotEmpty] public string City { get; set; } = ""; }
            [Validate] internal class Customer { public Address Home { get; set; } = new(); }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.CustomerValidator"));
    }

    [Fact]
    public void PublicModel_ValidatorStaysPublic()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Public, TypeAccessibility(compilation, "MyApp.JevOptionsValidator"));
    }

    [Fact]
    public void InternalOptionsModel_ValidateWithZeroAlloc_CompilesAndIsNotPublic()
    {
        // The probe from #184: an internal options class bound with ValidateWithZeroAlloc.
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;
            namespace MyApp;
            [Validate] internal sealed class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }

            internal static class Wiring
            {
                public static void Wire(IServiceCollection services)
                    => services.AddOptions<JevOptions>().ValidateWithZeroAlloc().ValidateOnStart();
            }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator(), new OptionsValidationEmitter());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "ZeroAlloc.Validation.Options.InternalZeroAllocOptionsValidationExtensions"));

        // With no public model there is nothing for a public extension class to hold, so it
        // must not appear in the consumer's API at all.
        Assert.Null(compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions"));
    }

    [Fact]
    public void MixedOptionsModels_SplitAcrossPublicAndInternalExtensionClasses()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;
            namespace MyApp;
            [Validate] public class PublicOptions { [NotEmpty] public string Host { get; set; } = ""; }
            [Validate] internal record InternalOptions { [NotEmpty] public string Key { get; init; } = ""; }

            internal static class Wiring
            {
                public static void Wire(IServiceCollection services)
                {
                    services.AddOptions<PublicOptions>().ValidateWithZeroAlloc();
                    services.AddOptions<InternalOptions>().ValidateWithZeroAlloc();
                }
            }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator(), new OptionsValidationEmitter());

        Assert.Empty(Errors(compilation));

        var publicClass = compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions");
        Assert.NotNull(publicClass);
        Assert.Equal(Accessibility.Public, publicClass.DeclaredAccessibility);
        Assert.Equal(["PublicOptions"], ExtendedModels(publicClass));

        var internalClass = compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.InternalZeroAllocOptionsValidationExtensions");
        Assert.NotNull(internalClass);
        Assert.Equal(Accessibility.Internal, internalClass.DeclaredAccessibility);
        Assert.Equal(["InternalOptions"], ExtendedModels(internalClass));
    }

    // ── ZeroAllocGeneratedAccessibility=Internal (issue #193, point 1) ─────────────────────

    [Fact]
    public void Unset_IsByteIdenticalToExplicitPublic()
    {
        // With the property unset or "Public", generated output must be byte-identical —
        // the FANOUT-RULES contract for this property across every ZeroAlloc package.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }
            """;

        var (unsetOutput, unsetDiagnostics) = RunGeneratorSource(source, null, new ValidatorGenerator());
        var (publicOutput, publicDiagnostics) = RunGeneratorSource(source, AccessibilityOptions("Public"), new ValidatorGenerator());

        Assert.DoesNotContain(unsetDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(publicDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var unsetGenerated = unsetOutput.SyntaxTrees.First(t => t.FilePath.Contains("JevOptionsValidator", StringComparison.Ordinal)).ToString();
        var publicGenerated = publicOutput.SyntaxTrees.First(t => t.FilePath.Contains("JevOptionsValidator", StringComparison.Ordinal)).ToString();
        Assert.Equal(unsetGenerated, publicGenerated);
    }

    [Fact]
    public void Internal_PublicModel_ValidatorBecomesInternal()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }
            """;

        var compilation = RunAndCompileWithAccessibility(source, "Internal", new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.JevOptionsValidator"));
    }

    [Fact]
    public void Internal_CaseInsensitive_IsAccepted()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }
            """;

        var compilation = RunAndCompileWithAccessibility(source, "iNtErNaL", new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.JevOptionsValidator"));
    }

    [Fact]
    public void InvalidValue_ReportsZV0019_AndFallsBackToPublic()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class JevOptions { [NotEmpty] public string ApiKey { get; set; } = ""; }
            """;

        var (output, diagnostics) = RunGeneratorSource(source, AccessibilityOptions("Priv4te"), new ValidatorGenerator());

        var zv0019s = new List<Diagnostic>();
        foreach (var d in diagnostics)
        {
            if (string.Equals(d.Id, "ZV0019", StringComparison.Ordinal))
                zv0019s.Add(d);
        }
#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        var zv0019 = Assert.Single(zv0019s);
#pragma warning restore HLQ005
        Assert.Equal(DiagnosticSeverity.Error, zv0019.Severity);
        Assert.Contains("Priv4te", zv0019.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        // The rest of the build still reflects today's (Public) behavior despite the typo.
        Assert.Equal(Accessibility.Public, TypeAccessibility(output, "MyApp.JevOptionsValidator"));
    }

    [Fact]
    public void Internal_PublicModelWithInternalNestedModel_Compiles()
    {
        // Point 3 of #193: a public model with a property whose type is an internal [Validate]
        // model. With Internal, the outer validator is internal too, so its constructor's
        // parameter (the nested internal validator) is no longer less accessible than the
        // constructor itself.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal class Address { [NotEmpty] public string City { get; set; } = ""; }
            [Validate] public class Customer { internal Address Home { get; set; } = new(); }
            """;

        var compilation = RunAndCompileWithAccessibility(source, "Internal", new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.CustomerValidator"));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.AddressValidator"));
    }

    [Fact]
    public void Unset_PublicModelWithInternalNestedModel_CompilesAndValidatorIsInternal()
    {
        // Point 3 of #193, extending the #184 rule: a public model's validator is public only
        // if the model itself is effectively public AND every nested [Validate] model it takes
        // as a constructor-injected dependency would itself resolve to a public validator.
        // Customer is public, but Address (its nested dependency) is internal, so
        // CustomerValidator's own constructor parameter would be less accessible than a public
        // constructor allows — CustomerValidator is internal instead, and the whole thing
        // compiles even with the property unset (today's default, no opt-in required).
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal class Address { [NotEmpty] public string City { get; set; } = ""; }
            [Validate] public class Customer { internal Address Home { get; set; } = new(); }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.CustomerValidator"));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.AddressValidator"));
    }

    [Fact]
    public void Unset_TwoLevelTransitiveNesting_AllThreeValidatorsBecomeInternal()
    {
        // Order (public) -> Customer (public) -> Address (internal). The rule is a fixed point
        // over the whole model graph, not just one level: Customer is itself public, but its
        // own validator is forced internal because it depends on Address's internal validator,
        // which in turn forces Order's validator internal too, even though Order and Customer
        // are both public models.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal class Address { [NotEmpty] public string City { get; set; } = ""; }
            [Validate] public class Customer { internal Address Home { get; set; } = new(); }
            [Validate] public class Order { public Customer Buyer { get; set; } = new(); }
            """;

        var compilation = RunAndCompile(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.OrderValidator"));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.CustomerValidator"));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.AddressValidator"));
    }

    [Fact]
    public void Unset_PublicModelWithInternalValidator_OptionsAndInjectStillCompile()
    {
        // Customer is public but, per the extended #184 rule, CustomerValidator is internal
        // because its nested Address dependency is internal. Options and Inject registration
        // code only ever reference the validator type inside a method BODY
        // (ValidatorFor<Customer>/CustomerValidator as TryAddSingleton's generic arguments),
        // never in a public member's own signature — a public method can freely reference an
        // internal type from the same assembly in its body. So neither emitter needs to key off
        // the validator's accessibility: only the model's own accessibility can ever appear in a
        // public signature (OptionsBuilder<Customer>, IServiceCollection), and Customer itself
        // is genuinely public. Options therefore correctly keeps routing Customer's
        // ValidateWithZeroAlloc() overload into the public extensions class — moving it to the
        // internal class would needlessly make a legitimate public model's public API
        // unreachable from outside the assembly.
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;
            namespace MyApp;
            [Validate] internal class Address { [NotEmpty] public string City { get; set; } = ""; }
            [Validate] public class Customer { internal Address Home { get; set; } = new(); }

            internal static class Wiring
            {
                public static void Wire(IServiceCollection services)
                {
                    services.AddOptions<Customer>().ValidateWithZeroAlloc();
                    services.AddZeroAllocValidators();
                }
            }
            """;

        var compilation = RunAndCompile(
            source,
            new ValidatorGenerator(),
            new OptionsValidationEmitter(),
            new global::ZeroAlloc.Validation.Inject.InjectGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(Accessibility.Internal, TypeAccessibility(compilation, "MyApp.CustomerValidator"));

        // Address is itself internal, so it always gets its own overload in the internal class,
        // independent of Customer. The question is only where Customer's own overload lands.
        var publicOptionsClass = compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions");
        var internalOptionsClass = compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.InternalZeroAllocOptionsValidationExtensions");
        Assert.NotNull(publicOptionsClass);
        Assert.NotNull(internalOptionsClass);
        Assert.Contains("Customer", ExtendedModels(publicOptionsClass), StringComparer.Ordinal);
        Assert.DoesNotContain("Customer", ExtendedModels(internalOptionsClass), StringComparer.Ordinal);

        // Inject's registration class stays public (mode unset) and its generated body compiles
        // fine despite referencing the internal CustomerValidator.
        Assert.Equal(Accessibility.Public, TypeAccessibility(compilation, "ZeroAllocValidatorRegistrationExtensions"));
    }

    [Fact]
    public void Unset_AspNetCoreGenerator_ClassStaysPublicRegardlessOfValidatorAccessibility()
    {
        // AspNetCoreFilterEmitter never inspects any model's or validator's accessibility —
        // ZeroAllocValidationServiceCollectionExtensions is emitted public purely based on
        // ZeroAllocGeneratedAccessibility, so a public model whose validator turns internal
        // because of a nested internal dependency does not change this class's own
        // accessibility. Uses RunGeneratorGetSources rather than a full compile, since this
        // test project does not reference the ASP.NET Core assemblies the generated code
        // itself depends on.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal class Address { [NotEmpty] public string City { get; set; } = ""; }
            [Validate] public class Customer { internal Address Home { get; set; } = new(); }
            """;

        var generated = RunGeneratorGetSources(source, globalOptions: null, new global::ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter())
            .First(s => s.Contains("ZeroAllocValidationServiceCollectionExtensions", StringComparison.Ordinal));

        Assert.Contains("public static class ZeroAllocValidationServiceCollectionExtensions", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Internal_OptionsModel_PublicModelRoutesToInternalExtensionsClass()
    {
        // With Internal, every model — public or not — goes into the internal extensions
        // class, and the public one is never emitted at all.
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;
            namespace MyApp;
            [Validate] public class PublicOptions { [NotEmpty] public string Host { get; set; } = ""; }

            internal static class Wiring
            {
                public static void Wire(IServiceCollection services)
                    => services.AddOptions<PublicOptions>().ValidateWithZeroAlloc();
            }
            """;

        var compilation = RunAndCompileWithAccessibility(source, "Internal", new ValidatorGenerator(), new OptionsValidationEmitter());

        Assert.Empty(Errors(compilation));

        var internalClass = compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.InternalZeroAllocOptionsValidationExtensions");
        Assert.NotNull(internalClass);
        Assert.Equal(Accessibility.Internal, internalClass.DeclaredAccessibility);
        Assert.Equal(["PublicOptions"], ExtendedModels(internalClass));

        Assert.Null(compilation.GetTypeByMetadataName("ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions"));
    }

    [Fact]
    public void Internal_InjectGenerator_RegistrationExtensionsClassBecomesInternal()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSources(source, AccessibilityOptions("Internal"), new global::ZeroAlloc.Validation.Inject.InjectGenerator())
            .First(s => s.Contains("ZeroAllocValidatorRegistrationExtensions", StringComparison.Ordinal));

        Assert.Contains("internal static class ZeroAllocValidatorRegistrationExtensions", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public static class ZeroAllocValidatorRegistrationExtensions", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Unset_InjectGenerator_RegistrationExtensionsClassStaysPublic()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSources(source, globalOptions: null, new global::ZeroAlloc.Validation.Inject.InjectGenerator())
            .First(s => s.Contains("ZeroAllocValidatorRegistrationExtensions", StringComparison.Ordinal));

        Assert.Contains("public static class ZeroAllocValidatorRegistrationExtensions", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Internal_AspNetCoreGenerator_ServiceCollectionExtensionsClassBecomesInternal()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSources(source, AccessibilityOptions("Internal"), new global::ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter())
            .First(s => s.Contains("ZeroAllocValidationServiceCollectionExtensions", StringComparison.Ordinal));

        Assert.Contains("internal static class ZeroAllocValidationServiceCollectionExtensions", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public static class ZeroAllocValidationServiceCollectionExtensions", generated, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> AccessibilityOptions(string value) =>
        new(StringComparer.Ordinal) { ["build_property.ZeroAllocGeneratedAccessibility"] = value };

    /// <summary>Runs the generators with the given global options and returns only the compiled output.</summary>
    private static Compilation RunAndCompileWithAccessibility(string source, string accessibilityValue, params IIncrementalGenerator[] generators)
        => RunGeneratorSource(source, AccessibilityOptions(accessibilityValue), generators).Compilation;

    private static List<string> ExtendedModels(INamedTypeSymbol extensionClass)
        => extensionClass.GetMembers("ValidateWithZeroAlloc")
            .OfType<IMethodSymbol>()
            .Select(m => ((INamedTypeSymbol)m.Parameters[0].Type).TypeArguments[0].Name)
            .ToList();

    private static Accessibility TypeAccessibility(Compilation compilation, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        Assert.NotNull(type);
        return type.DeclaredAccessibility;
    }

    private static List<string> Errors(Compilation compilation)
    {
        var errors = new List<string>();
        foreach (var d in compilation.GetDiagnostics())
        {
            // Id and message only: the default formatting leads with the generated file
            // path, which truncates the useful part out of an assertion failure.
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add($"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        return errors;
    }

    /// <summary>
    /// Runs the generators over <paramref name="source"/> and returns the compilation with
    /// their output added, referencing this test host's own dependencies, which include the
    /// options and dependency injection assemblies.
    /// </summary>
    private static Compilation RunAndCompile(string source, params IIncrementalGenerator[] generators)
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

        CSharpGeneratorDriver
            .Create(generators)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        return output;
    }

    /// <summary>
    /// Runs the generators over <paramref name="source"/> with the given global MSBuild
    /// properties (i.e. those declared via CompilerVisibleProperty) and returns both the
    /// compiled output and the diagnostics the generators reported.
    /// </summary>
    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) RunGeneratorSource(
        string source, IReadOnlyDictionary<string, string>? globalOptions, params IIncrementalGenerator[] generators)
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

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators);
        if (globalOptions is not null)
            driver = driver.WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(globalOptions));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (output, diagnostics);
    }

    /// <summary>Like <see cref="RunGeneratorSource"/>, but returns each generated file's text.</summary>
    private static IReadOnlyList<string> RunGeneratorGetSources(
        string source, IReadOnlyDictionary<string, string>? globalOptions, params IIncrementalGenerator[] generators)
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

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators);
        if (globalOptions is not null)
            driver = driver.WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(globalOptions));

        var result = driver.RunGenerators(compilation).GetRunResult();
        return result.GeneratedTrees.Select(t => t.ToString()).ToList();
    }

    /// <summary>
    /// Minimal <see cref="AnalyzerConfigOptionsProvider"/> that exposes a fixed set of "global"
    /// MSBuild properties (i.e. those declared via CompilerVisibleProperty), matching how the
    /// real SDK surfaces build_property.* values to source generators from the package's
    /// build/buildTransitive props.
    /// </summary>
    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestAnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalOptions)
        {
            _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _globalOptions;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values) => _values = values;

        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    }
}
