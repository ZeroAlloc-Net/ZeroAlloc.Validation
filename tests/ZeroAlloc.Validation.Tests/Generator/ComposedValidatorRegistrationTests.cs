using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    private static MetadataReference CompileLibrary(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Lib",
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
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static (Compilation Output, string Registration) Run(string source, MetadataReference? extra = null)
    {
        var references = References().ToList();
        if (extra is not null)
            references.Add(extra);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new ValidatorGenerator(), new InjectGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var registration = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .First(s => string.Equals(s.HintName, RegistrationHintName, StringComparison.Ordinal))
            .SourceText.ToString();

        return (output, registration);
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
