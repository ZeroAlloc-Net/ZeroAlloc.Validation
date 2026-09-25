using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Validation.Generator;
using ZeroAlloc.Validation.Inject;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Guards issue #246: the DI glue registers every validator a composed validator's constructor
/// takes, so the container can build it from <c>AddZeroAllocValidators()</c> alone. A nested or
/// collection <c>[Validate]</c> model is taken as <c>ValidatorFor&lt;T&gt;</c>, and a
/// <c>[ValidateWith]</c> validator by its own type.
/// </summary>
public class ComposedValidatorRegistrationTests
{
    private const string RegistrationHintName = "ZeroAlloc.Validation.ZeroAllocValidatorRegistrationExtensions.g.cs";

    [Fact]
    public void ValidateWithValidator_IsRegisteredByItsOwnType()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace MyApp;
            public class Money { public decimal Amount { get; set; } }
            public sealed class MoneyChecker : ValidatorFor<Money>
            {
                public override ValidationResult Validate(Money instance) =>
                    new ValidationResult(System.Array.Empty<ValidationFailure>());
            }
            [Validate] public class Invoice
            {
                [ValidateWith(typeof(MoneyChecker))] public Money Total { get; set; } = new();
                [ValidateWith(typeof(MoneyChecker))] public List<Money> Lines { get; set; } = new();
            }
            """;

        var (output, registration) = Run(source);

        Assert.Empty(Errors(output));
        Assert.Equal(1, Occurrences(registration, "services.TryAddSingleton<global::MyApp.MoneyChecker>();"));
    }

    [Fact]
    public void AbstractValidateWithValidator_IsNotRegistered()
    {
        // The container cannot construct an abstract type, and with ValidateOnBuild a
        // registration for one fails the whole provider. The caller registers an implementation.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            public class Money { public decimal Amount { get; set; } }
            public abstract class MoneyChecker : ValidatorFor<Money> { }
            [Validate] public class Invoice { [ValidateWith(typeof(MoneyChecker))] public Money Total { get; set; } = new(); }
            """;

        var (output, registration) = Run(source);

        Assert.Empty(Errors(output));
        Assert.DoesNotContain("MoneyChecker", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedModelFromReferencedAssembly_ItsValidatorIsRegistered()
    {
        // The nested model's validator is generated in the library, so the application's own
        // [Validate] scan never sees it; the glue follows the composed validator's dependencies.
        var library = """
            using ZeroAlloc.Validation;
            namespace Lib;
            [Validate] public class Address { [NotEmpty] public string City { get; set; } = ""; }
            """;
        var application = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Order
            {
                public Lib.Address Ship { get; set; } = new();
                public List<Lib.Address> Stops { get; set; } = new();
            }
            """;

        var (output, registration) = Run(application, CompileLibrary(library));

        Assert.Empty(Errors(output));
        Assert.Equal(1, Occurrences(
            registration,
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Lib.Address>, global::Lib.AddressValidator>();"));
        Assert.Equal(1, Occurrences(
            registration,
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::MyApp.Order>, global::MyApp.OrderValidator>();"));
    }

    [Fact]
    public void NestedModelFromReferencedAssembly_WithInternalValidator_IsNotRegistered()
    {
        // Address is public, but its validator is internal because its own constructor takes
        // ValidatorFor<Zone>, and Zone is internal. The application cannot name that validator,
        // so it leaves it to the library's own registration; its own validator still compiles,
        // since it takes ValidatorFor<Lib.Address>.
        var library = """
            using ZeroAlloc.Validation;
            namespace Lib;
            [Validate] internal class Zone { [NotEmpty] public string Code { get; set; } = ""; }
            [Validate] public class Address
            {
                [NotEmpty] public string City { get; set; } = "";
                internal Zone Zone { get; set; } = new();
            }
            """;
        var application = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Order { public Lib.Address Ship { get; set; } = new(); }
            """;

        var (output, registration) = Run(application, CompileLibrary(library));

        Assert.Empty(Errors(output));
        Assert.DoesNotContain("AddressValidator", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("Zone", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedModelFromReferencedAssembly_InKeywordNamespace_ItsValidatorIsRegistered()
    {
        // The validator is looked up by metadata name, where namespace segments are never
        // escaped: class.Models, not @class.Models.
        var library = """
            using ZeroAlloc.Validation;
            namespace @class.Models;
            [Validate] public class Address { [NotEmpty] public string City { get; set; } = ""; }
            """;
        var application = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Order { public global::@class.Models.Address Ship { get; set; } = new(); }
            """;

        var (output, registration) = Run(application, CompileLibrary(library));

        Assert.Empty(Errors(output));
        Assert.Equal(1, Occurrences(
            registration,
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::@class.Models.Address>, global::@class.Models.AddressValidator>();"));
    }

    [Fact]
    public void NestedModelChainFromReferencedAssembly_EveryLevelIsRegisteredAndResolves()
    {
        var library = """
            using ZeroAlloc.Validation;
            namespace Chain246.Lib;
            [Validate] public class Zone { [NotEmpty] public string Code { get; set; } = ""; }
            [Validate] public class Address { public Zone Zone { get; set; } = new(); }
            """;
        var application = """
            using ZeroAlloc.Validation;
            namespace Chain246.App;
            [Validate] public class Order { public Chain246.Lib.Address Ship { get; set; } = new(); }
            """;

        var (libraryReference, libraryImage) = CompileLibraryImage(library, "Chain246.Lib");
        var (output, registration, _) = RunWithDiagnostics(application, "Chain246.App", libraryReference);

        Assert.Empty(Errors(output));
        Assert.Equal(1, Occurrences(
            registration,
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Chain246.Lib.Address>, global::Chain246.Lib.AddressValidator>();"));
        Assert.Equal(1, Occurrences(
            registration,
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Chain246.Lib.Zone>, global::Chain246.Lib.ZoneValidator>();"));

        var (provider, assembly) = BuildProvider(output, libraryImage);
        using (provider)
        {
            var result = ResolveAndValidate(provider, assembly.GetType("Chain246.App.Order")!);
            Assert.Equal(1, result.Failures.Length);
            Assert.Equal("Ship.Zone.Code", result.Failures[0].PropertyName);
        }
    }

    [Fact]
    public void ValidateWithReferencedGeneratedValidator_ItsModelsDependenciesAreRegistered()
    {
        // [ValidateWith] names the library's own generated AddressValidator, which ZV0011 flags.
        // It is taken by its own type, and its constructor takes ValidatorFor<Zone>, so Address
        // is followed and Zone registered too.
        var library = """
            using ZeroAlloc.Validation;
            namespace Lib;
            [Validate] public class Zone { [NotEmpty] public string Code { get; set; } = ""; }
            [Validate] public class Address { public Zone Zone { get; set; } = new(); }
            """;
        var application = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Order
            {
                [ValidateWith(typeof(Lib.AddressValidator))] public Lib.Address Ship { get; set; } = new();
            }
            """;

        var (output, registration) = Run(application, CompileLibrary(library));

        Assert.Empty(Errors(output));
        Assert.Equal(1, Occurrences(registration, "services.TryAddSingleton<global::Lib.AddressValidator>();"));
        Assert.Equal(1, Occurrences(
            registration,
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Lib.Zone>, global::Lib.ZoneValidator>();"));
    }

    [Theory]
    [InlineData("Zv11Scalar", "public Address Home { get; set; } = new();", "Home.Street")]
    [InlineData("Zv11Collection", "public List<Address> Homes { get; set; } = [new()];", "Homes[0].Street")]
    public void ValidateWithSameAssemblyGeneratedValidator_TakesTheAutoComposedPath(string ns, string property, string failedPath)
    {
        // The model's own generated validator is an error type to every generator, so it raised
        // a false ZV0012, made the outer validator internal, and named a constructor parameter
        // the glue could not register. It is now the auto-composed path; ZV0011 still says the
        // attribute is redundant on a scalar property.
        var source = $$"""
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace {{ns}};
            [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
            [Validate] public class Customer
            {
                [ValidateWith(typeof(AddressValidator))]
                {{property}}
            }
            """;

        var (output, registration, diagnostics) = RunWithDiagnostics(source, ns);

        Assert.Empty(Errors(output));
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZV0012", StringComparison.Ordinal));
        if (string.Equals(ns, "Zv11Scalar", StringComparison.Ordinal))
            Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZV0011", StringComparison.Ordinal));

        var customer = output.GetTypeByMetadataName($"{ns}.CustomerValidator");
        Assert.NotNull(customer);
        Assert.Equal(Accessibility.Public, customer.DeclaredAccessibility);
        // One constructor, taking one ValidatorFor<Address>.
        var constructors = string.Join(
            ";",
            customer.InstanceConstructors.Select(c => string.Join(",", c.Parameters.Select(p => p.Type.ToDisplayString()))));
        Assert.Equal($"ZeroAlloc.Validation.ValidatorFor<{ns}.Address>", constructors);
        Assert.DoesNotContain($"TryAddSingleton<global::{ns}.AddressValidator>()", registration, StringComparison.Ordinal);

        var (provider, assembly) = BuildProvider(output);
        using (provider)
        {
            var result = ResolveAndValidate(provider, assembly.GetType($"{ns}.Customer")!);
            Assert.Equal(1, result.Failures.Length);
            Assert.Equal(failedPath, result.Failures[0].PropertyName);
        }
    }

    [Fact]
    public void ExcludedBaseValidateWith_IsNotRegistered_AndDoesNotMakeTheValidatorInternal()
    {
        // IncludeBaseProperties = false leaves Total out of the constructor, so its internal
        // [ValidateWith] validator is neither registered nor an accessibility constraint.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            public class Money { public decimal Amount { get; set; } }
            internal sealed class MoneyChecker : ValidatorFor<Money>
            {
                public override ValidationResult Validate(Money instance) =>
                    new ValidationResult(System.Array.Empty<ValidationFailure>());
            }
            public class Base { [ValidateWith(typeof(MoneyChecker))] public Money Total { get; set; } = new(); }
            [Validate(IncludeBaseProperties = false)]
            public class Derived : Base { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var (output, registration) = Run(source);

        Assert.Empty(Errors(output));
        Assert.DoesNotContain("MoneyChecker", registration, StringComparison.Ordinal);
        Assert.Equal(Accessibility.Public, output.GetTypeByMetadataName("MyApp.DerivedValidator")!.DeclaredAccessibility);
    }

    [Fact]
    public void ValidateWithGenericOverInternalArgument_ValidatorIsInternal_AndRegistered()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            public class Money { public decimal Amount { get; set; } }
            internal sealed class Currency { }
            public sealed class MoneyChecker<T> : ValidatorFor<Money>
            {
                public override ValidationResult Validate(Money instance) =>
                    new ValidationResult(System.Array.Empty<ValidationFailure>());
            }
            [Validate] public class Invoice
            {
                [ValidateWith(typeof(MoneyChecker<Currency>))] public Money Total { get; set; } = new();
            }
            """;

        var (output, registration) = Run(source);

        Assert.Empty(Errors(output));
        Assert.Equal(Accessibility.Internal, output.GetTypeByMetadataName("MyApp.InvoiceValidator")!.DeclaredAccessibility);
        Assert.Equal(1, Occurrences(registration, "services.TryAddSingleton<global::MyApp.MoneyChecker<global::MyApp.Currency>>();"));
    }

    [Fact]
    public void PublicModelWithCollectionOfInternalModel_CompilesCleanlyAndValidatorIsInternal()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] internal class Line { [NotEmpty] public string Sku { get; set; } = ""; }
            [Validate] public class Cart { internal IList<Line> Lines { get; set; } = new List<Line>(); }
            """;

        var (output, registration, diagnostics) = RunWithDiagnostics(source, "TestAssembly");

        Assert.Empty(Errors(output));
        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        Assert.Equal(Accessibility.Internal, output.GetTypeByMetadataName("MyApp.CartValidator")!.DeclaredAccessibility);
        Assert.Equal(1, Occurrences(
            registration,
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::MyApp.Line>, global::MyApp.LineValidator>();"));
    }

    private static MetadataReference CompileLibrary(string source) => CompileLibraryImage(source, "Lib").Reference;

    /// <summary>
    /// Compiles <paramref name="source"/> with ValidatorGenerator into an assembly named
    /// <paramref name="assemblyName"/>, returning both a reference to it and its image, so a
    /// test can also load it.
    /// </summary>
    private static (MetadataReference Reference, byte[] Image) CompileLibraryImage(string source, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver
            .Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        Assert.Empty(Errors(output));
        using var stream = new System.IO.MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success);
        var image = stream.ToArray();
        return (MetadataReference.CreateFromImage(image), image);
    }

    private static (Compilation Output, string Registration) Run(string source, params MetadataReference[] extra)
    {
        var (output, registration, _) = RunWithDiagnostics(source, "TestAssembly", extra);
        return (output, registration);
    }

    private static (Compilation Output, string Registration, ImmutableArray<Diagnostic> Diagnostics) RunWithDiagnostics(
        string source, string assemblyName, params MetadataReference[] extra)
    {
        var references = References().ToList();
        references.AddRange(extra);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new ValidatorGenerator(), new InjectGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var registration = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .First(s => string.Equals(s.HintName, RegistrationHintName, StringComparison.Ordinal))
            .SourceText.ToString();

        return (output, registration, diagnostics);
    }

    /// <summary>
    /// Emits <paramref name="output"/> and loads it, after every image in
    /// <paramref name="libraries"/>, into the default load context, calls its generated
    /// <c>AddZeroAllocValidators()</c> and builds a provider that validates every registration on
    /// build. Each test uses its own assembly names, since the default context keeps what it loads.
    /// </summary>
    private static (ServiceProvider Provider, Assembly Assembly) BuildProvider(Compilation output, params byte[][] libraries)
    {
        foreach (var library in libraries)
        {
            using var libraryStream = new System.IO.MemoryStream(library);
            AssemblyLoadContext.Default.LoadFromStream(libraryStream);
        }

        using var stream = new System.IO.MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(d => d.ToString())));
        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);

        var services = new ServiceCollection();
        assembly.GetType("ZeroAllocValidatorRegistrationExtensions")!
            .GetMethod("AddZeroAllocValidators")!
            .Invoke(null, [services]);
        return (services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }), assembly);
    }

    /// <summary>Resolves <c>ValidatorFor&lt;model&gt;</c> and validates a new instance of the model.</summary>
    private static ValidationResult ResolveAndValidate(ServiceProvider provider, Type model)
    {
        var validator = provider.GetRequiredService(typeof(ValidatorFor<>).MakeGenericType(model));
        return (ValidationResult)validator.GetType().GetMethod("Validate", [model])!
            .Invoke(validator, [Activator.CreateInstance(model)])!;
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static IEnumerable<MetadataReference> References() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(System.IO.Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

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
}
