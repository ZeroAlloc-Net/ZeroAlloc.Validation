using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// The generators' pipelines carry only values that compare by value, so an edit that does not
/// change what a generator produces leaves its output steps cached, issue #209. Before, the
/// validator pipeline carried the model's symbol and the compilation, and the companion
/// generators the models' symbols, so every edit, every IDE keystroke included, regenerated
/// every validator and every registration file.
/// </summary>
public class IncrementalCachingTests
{
    private const string OrderSource = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Order : OrderBase
        {
            [NotEmpty] public string Reference { get; set; } = "";
            public Customer? Customer { get; set; }
        }
        """;

    private const string OrderBaseSource = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        public class OrderBase
        {
            [NotEmpty] public string Channel { get; set; } = "";
        }
        """;

    private const string CustomerSource = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Customer
        {
            [NotEmpty] public string Name { get; set; } = "";
        }
        """;

    private const string UnrelatedSource = """
        namespace TestModels;

        public class Unrelated
        {
            public int Value { get; set; }
        }
        """;

    private const string UnrelatedEdited = """
        namespace TestModels;

        public class Unrelated
        {
            public int Value { get; set; }
            public int Other { get; set; }
        }
        """;

    private const string OrderHint = "TestModels.OrderValidator.g.cs";
    private const string CustomerHint = "TestModels.CustomerValidator.g.cs";

    // The companion generators' tracking names. Their constants are internal to assemblies this
    // test project cannot see into, so they are repeated here.
    public static TheoryData<string, string> CompanionGenerators() => new()
    {
        { "Inject", "ValidatorRegistrations" },
        { "Options", "OptionsValidationModels" },
        { "AspNetCore", "AspNetCoreValidationModels" },
    };

    [Fact]
    public void Validator_outputs_are_cached_when_the_compilation_is_cloned()
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(new ValidatorGenerator()).RunGenerators(compilation);

        var result = driver.RunGenerators(compilation.Clone()).GetRunResult().Results[0];

        AssertAllCached(result, ValidatorGenerator.ValidatorTrackingName, expectedOutputs: 2);
    }

    [Fact]
    public void Validator_outputs_are_cached_when_an_unrelated_file_is_added()
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(new ValidatorGenerator()).RunGenerators(compilation);

        var added = compilation.AddSyntaxTrees(Parse("namespace TestModels; public class Added { }", "Added.cs"));
        var result = driver.RunGenerators(added).GetRunResult().Results[0];

        AssertAllCached(result, ValidatorGenerator.ValidatorTrackingName, expectedOutputs: 2);
    }

    [Fact]
    public void Validator_outputs_are_cached_when_an_unrelated_file_is_edited()
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(new ValidatorGenerator()).RunGenerators(compilation);
        var before = GeneratedSources(driver.GetRunResult());

        var run = driver.RunGenerators(Edit(compilation, "Unrelated.cs", UnrelatedEdited)).GetRunResult();

        AssertAllCached(run.Results[0], ValidatorGenerator.ValidatorTrackingName, expectedOutputs: 2);
        Assert.Equal(before, GeneratedSources(run));
    }

    [Fact]
    public void Only_the_validator_of_the_edited_model_file_is_regenerated()
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(new ValidatorGenerator()).RunGenerators(compilation);

        var before = GeneratorTestHelper.GetGeneratedSource(driver.GetRunResult(), CustomerHint);

        var edited = Edit(compilation, "Customer.cs", CustomerSource.Replace(
            "[NotEmpty] public string Name",
            "[NotEmpty][MaxLength(50)] public string Name",
            StringComparison.Ordinal));
        var run = driver.RunGenerators(edited).GetRunResult();
        var result = run.Results[0];

        // Order's file did not change, and neither did its validator: cached.
        AssertCached(ValidatorStepReason(result, OrderHint));
        AssertCached(OutputStepReason(result, OrderHint));

        // Customer's file changed: its validator is generated and added again.
        Assert.Equal(IncrementalStepRunReason.Modified, ValidatorStepReason(result, CustomerHint));
        Assert.Equal(IncrementalStepRunReason.Modified, OutputStepReason(result, CustomerHint));
        Assert.NotEqual(before, GeneratorTestHelper.GetGeneratedSource(run, CustomerHint), StringComparer.Ordinal);
    }

    [Fact]
    public void Validator_is_regenerated_when_a_base_type_in_another_file_changes()
    {
        // Order's own file is unchanged, but its validator reads the rules of its base type, so
        // the generation reruns against every compilation and picks up the edit.
        var compilation = CreateCompilation();
        var driver = CreateDriver(new ValidatorGenerator()).RunGenerators(compilation);
        var before = GeneratorTestHelper.GetGeneratedSource(driver.GetRunResult(), OrderHint);

        // The edit keeps an attribute list in the file. One added to a file that had none makes
        // Roslyn's own ForAttributeWithMetadataName report the [Validate] declarations of every
        // later file as removed and new, whatever the generator does with them.
        var edited = Edit(compilation, "OrderBase.cs", OrderBaseSource.Replace(
            "[NotEmpty] public string Channel",
            "[NotEmpty][MaxLength(10)] public string Channel",
            StringComparison.Ordinal));
        var run = driver.RunGenerators(edited).GetRunResult();
        Assert.Equal(IncrementalStepRunReason.Modified, ValidatorStepReason(run.Results[0], OrderHint));
        Assert.NotEqual(before, GeneratorTestHelper.GetGeneratedSource(run, OrderHint), StringComparer.Ordinal);
        AssertCached(ValidatorStepReason(run.Results[0], CustomerHint));
    }

    private const string EntitySource = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        public sealed class TaggedAttribute : ValidationAttribute<string?>
        {
            public override bool IsValid(string? value) => value is not null;
        }

        public class Entity
        {
            [Tagged(Message = "{nope}")] public string? Tenant { get; set; }
        }
        """;

    private const string BookingSource = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Booking : Entity
        {
            [NotEmpty] public string Reference { get; set; } = "";
        }
        """;

    private const string InvoiceSource = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Invoice : Entity { }
        """;

    private const string EntityKey = "global::TestModels.Entity";

    [Fact]
    public void Plain_base_type_diagnostics_stay_cached_when_a_derived_model_is_edited()
    {
        // Issue #290: the usages on a plain base type are reported by the base type's own step, so
        // an edit to one of the models deriving from it does not report them again.
        var compilation = CSharpCompilation.Create(
            "IncrementalCachingTests",
            [Parse(EntitySource, "Entity.cs"), Parse(BookingSource, "Booking.cs"), Parse(InvoiceSource, "Invoice.cs")],
            GeneratorTestHelper.MinimalReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateDriver(new ValidatorGenerator()).RunGenerators(compilation);
        Assert.Equal(1, Count(driver.GetRunResult(), "ZV0022"));

        var edited = Edit(compilation, "Booking.cs", BookingSource.Replace(
            "[NotEmpty] public string Reference",
            "[NotEmpty][MaxLength(50)] public string Reference",
            StringComparison.Ordinal));
        driver = driver.RunGenerators(edited);
        var run = driver.GetRunResult();
        var result = run.Results[0];

        AssertCached(DeclaringTypeStepReason(result, EntityKey));
        AssertCached(DeclaringTypeOutputStepReason(result, EntityKey));
        Assert.Equal(1, Count(run, "ZV0022"));

        // The base type's own edit is picked up.
        var fixedEntity = Edit(edited, "Entity.cs", EntitySource.Replace("{nope}", "{PropertyName}", StringComparison.Ordinal));
        run = driver.RunGenerators(fixedEntity).GetRunResult();
        Assert.Equal(IncrementalStepRunReason.Modified, DeclaringTypeStepReason(run.Results[0], EntityKey));
        Assert.Equal(0, Count(run, "ZV0022"));
    }

    [Fact]
    public void Pragma_still_suppresses_warnings_reported_from_the_cache()
    {
        // ZV0032 comes from the validator pipeline and ZV0026 from the [RuleMessage] one. Both are
        // Warning-severity and both are carried as a tree and a span, so the location they are
        // reported at on a cached run is still in the tree, where the pragma applies to it.
        const string source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public class Request
            {
            #pragma warning disable ZV0032
                [Must(nameof(Ok))] public string? Code { get; set; }
            #pragma warning restore ZV0032

                public bool Ok(string value) => true;
            }

            #pragma warning disable ZV0026
            [RuleMessage("{PropertyName} must not be blank.")]
            #pragma warning restore ZV0026
            public sealed class NotARule { }
            """;
        var compilation = CSharpCompilation.Create(
            "IncrementalCachingTests",
            [Parse(source, "Request.cs"), Parse(UnrelatedSource, "Unrelated.cs")],
            GeneratorTestHelper.MinimalReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateDriver(new ValidatorGenerator()).RunGenerators(compilation);
        AssertAllSuppressed(driver.GetRunResult());

        var run = driver.RunGenerators(Edit(compilation, "Unrelated.cs", UnrelatedEdited)).GetRunResult();

        AssertAllCached(run.Results[0], ValidatorGenerator.ValidatorTrackingName, expectedOutputs: 1);
        AssertAllSuppressed(run);
    }

    [Theory]
    [MemberData(nameof(CompanionGenerators))]
    public void Companion_output_is_cached_when_an_unrelated_file_is_edited(string generator, string trackingName)
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(Companion(generator)).RunGenerators(compilation);
        var before = GeneratedSources(driver.GetRunResult());

        var run = driver.RunGenerators(Edit(compilation, "Unrelated.cs", UnrelatedEdited)).GetRunResult();

        AssertAllCached(run.Results[0], trackingName, expectedOutputs: 1);
        Assert.Equal(before, GeneratedSources(run));
    }

    [Theory]
    [MemberData(nameof(CompanionGenerators))]
    public void Companion_output_is_cached_when_a_model_edit_changes_no_registration(string generator, string trackingName)
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(Companion(generator)).RunGenerators(compilation);

        // A new rule changes Customer's validator, not how it is registered.
        var edited = Edit(compilation, "Customer.cs", CustomerSource.Replace(
            "[NotEmpty] public string Name",
            "[NotEmpty][MaxLength(50)] public string Name",
            StringComparison.Ordinal));
        var result = driver.RunGenerators(edited).GetRunResult().Results[0];

        AssertAllCached(result, trackingName, expectedOutputs: 1);
    }

    [Theory]
    [MemberData(nameof(CompanionGenerators))]
    public void Companion_output_is_regenerated_when_a_model_is_added(string generator, string trackingName)
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(Companion(generator)).RunGenerators(compilation);

        var edited = Edit(compilation, "Customer.cs", CustomerSource + """

            [Validate]
            public class Invoice
            {
                [NotEmpty] public string Number { get; set; } = "";
            }
            """);
        var run = driver.RunGenerators(edited).GetRunResult();

        Assert.Contains(
            run.Results[0].TrackedSteps[trackingName].SelectMany(s => s.Outputs),
            o => o.Reason == IncrementalStepRunReason.Modified);
        Assert.Contains(GeneratedSources(run), s => s.Contains("global::TestModels.Invoice", StringComparison.Ordinal));
    }

    [Fact]
    public void Registrations_follow_an_edit_to_a_ValidateWith_validator_in_another_file()
    {
        // The registration of a [ValidateWith] validator depends on that validator being
        // constructible, which is decided in its own file, not the model's.
        const string model = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class Address { public string Street { get; set; } = ""; }

            [Validate]
            public class Shipment
            {
                [ValidateWith(typeof(AddressValidator))] public Address Address { get; set; } = new();
            }
            """;
        const string validator = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class AddressValidator : ValidatorFor<Address>
            {
                public override ValidationResult Validate(Address instance) => default;
            }
            """;
        var compilation = CSharpCompilation.Create(
            "IncrementalCachingTests",
            [Parse(model, "Shipment.cs"), Parse(validator, "AddressValidator.cs")],
            GeneratorTestHelper.MinimalReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CreateDriver(new ZeroAlloc.Validation.Inject.InjectGenerator()).RunGenerators(compilation);
        const string registration = "services.TryAddSingleton<global::TestModels.AddressValidator>();";
        Assert.Contains(GeneratedSources(driver.GetRunResult()), s => s.Contains(registration, StringComparison.Ordinal));

        var edited = Edit(compilation, "AddressValidator.cs", validator.Replace(
            "public class AddressValidator", "public abstract class AddressValidator", StringComparison.Ordinal));
        var run = driver.RunGenerators(edited).GetRunResult();

        Assert.DoesNotContain(GeneratedSources(run), s => s.Contains(registration, StringComparison.Ordinal));
    }

    private const string GenericPageSource = """
        using System.Collections.Generic;
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Page<TItem> where TItem : class
        {
            [NotEmpty] public string Title { get; set; } = "";
            public List<Line<TItem>> Lines { get; set; } = new();
        }

        [Validate]
        public class Line<TItem> where TItem : class
        {
            [GreaterThan(0)] public int Quantity { get; set; }
        }
        """;

    private const string OrderPageSource = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        public class Product { }

        [Validate]
        public class OrderPage
        {
            public Page<Product> Page { get; set; } = new();
        }
        """;

    private static CSharpCompilation CreateGenericCompilation() =>
        CSharpCompilation.Create(
            "IncrementalCachingTests",
            [Parse(GenericPageSource, "Page.cs"), Parse(OrderPageSource, "OrderPage.cs"), Parse(UnrelatedSource, "Unrelated.cs")],
            GeneratorTestHelper.MinimalReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    [Fact]
    public void Generic_validator_outputs_are_cached_when_an_unrelated_file_is_edited()
    {
        // Issue #238: a generic model's validator, and the closed composition of it, are values
        // like any other and stay cached.
        var compilation = CreateGenericCompilation();
        var driver = CreateDriver(new ValidatorGenerator()).RunGenerators(compilation);
        var before = GeneratedSources(driver.GetRunResult());
        Assert.Contains(before, s => s.StartsWith("TestModels.PageValidator`1.g.cs", StringComparison.Ordinal));

        var run = driver.RunGenerators(Edit(compilation, "Unrelated.cs", UnrelatedEdited)).GetRunResult();

        AssertAllCached(run.Results[0], ValidatorGenerator.ValidatorTrackingName, expectedOutputs: 3);
        Assert.Equal(before, GeneratedSources(run));
    }

    [Theory]
    [MemberData(nameof(CompanionGenerators))]
    public void Companion_output_with_a_generic_model_is_cached_when_an_edit_changes_no_registration(string generator, string trackingName)
    {
        var compilation = CreateGenericCompilation();
        var driver = CreateDriver(Companion(generator)).RunGenerators(compilation);
        var before = GeneratedSources(driver.GetRunResult());
        Assert.Contains(before, s => s.Contains("global::TestModels.PageValidator<global::TestModels.Product>", StringComparison.Ordinal));

        var unrelated = driver.RunGenerators(Edit(compilation, "Unrelated.cs", UnrelatedEdited)).GetRunResult();
        AssertAllCached(unrelated.Results[0], trackingName, expectedOutputs: 1);
        Assert.Equal(before, GeneratedSources(unrelated));

        // A new rule on the generic model changes its validator, not how its closing is registered.
        var edited = Edit(compilation, "Page.cs", GenericPageSource.Replace(
            "[NotEmpty] public string Title",
            "[NotEmpty][MaxLength(50)] public string Title",
            StringComparison.Ordinal));
        AssertAllCached(driver.RunGenerators(edited).GetRunResult().Results[0], trackingName, expectedOutputs: 1);
    }

    [Theory]
    [MemberData(nameof(CompanionGenerators))]
    public void Companion_output_follows_a_new_closing_of_a_generic_model(string generator, string trackingName)
    {
        var compilation = CreateGenericCompilation();
        var driver = CreateDriver(Companion(generator)).RunGenerators(compilation);

        var edited = Edit(compilation, "OrderPage.cs", OrderPageSource.Replace(
            "public Page<Product> Page { get; set; } = new();",
            "public Page<Product> Page { get; set; } = new();\n    public Page<string> Names { get; set; } = new();",
            StringComparison.Ordinal));
        var run = driver.RunGenerators(edited).GetRunResult();

        Assert.Contains(
            run.Results[0].TrackedSteps[trackingName].SelectMany(s => s.Outputs),
            o => o.Reason == IncrementalStepRunReason.Modified);
        Assert.Contains(GeneratedSources(run), s => s.Contains("global::TestModels.PageValidator<string>", StringComparison.Ordinal));
    }

    private static IIncrementalGenerator Companion(string name) => name switch
    {
        "Inject" => new ZeroAlloc.Validation.Inject.InjectGenerator(),
        "Options" => new ZeroAlloc.Validation.Options.Generator.OptionsValidationEmitter(),
        _ => new ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter(),
    };

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            "IncrementalCachingTests",
            [
                Parse(OrderSource, "Order.cs"),
                Parse(OrderBaseSource, "OrderBase.cs"),
                Parse(CustomerSource, "Customer.cs"),
                Parse(UnrelatedSource, "Unrelated.cs"),
            ],
            GeneratorTestHelper.MinimalReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    private static SyntaxTree Parse(string source, string path) => CSharpSyntaxTree.ParseText(source, path: path);

    private static Compilation Edit(Compilation compilation, string path, string newSource)
    {
        var old = Only(compilation.SyntaxTrees, t => string.Equals(t.FilePath, path, StringComparison.Ordinal));
        return compilation.ReplaceSyntaxTree(old, Parse(newSource, path));
    }

    private static GeneratorDriver CreateDriver(IIncrementalGenerator generator) =>
        CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static List<string> GeneratedSources(GeneratorDriverRunResult result) =>
        result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.HintName + "\n" + s.SourceText).ToList();

    /// <summary>
    /// Every output of the named step and of every source-output step is cached or unchanged,
    /// and the named step produced <paramref name="expectedOutputs"/> of them, so the assertion
    /// cannot pass vacuously.
    /// </summary>
    private static void AssertAllCached(GeneratorRunResult result, string trackingName, int expectedOutputs)
    {
        var step = result.TrackedSteps[trackingName].SelectMany(s => s.Outputs).ToList();
        Assert.Equal(expectedOutputs, step.Count);
        Assert.All(step, o => AssertCached(o.Reason));

        var outputs = result.TrackedOutputSteps.SelectMany(kv => kv.Value).SelectMany(s => s.Outputs).ToList();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, o => AssertCached(o.Reason));
    }

    private static void AssertCached(IncrementalStepRunReason reason) =>
        Assert.True(
            reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected a cached or unchanged step, got {reason}");

    private static int Count(GeneratorDriverRunResult result, string id) =>
        result.Diagnostics.Count(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    private static IncrementalStepRunReason DeclaringTypeStepReason(GeneratorRunResult result, string key) =>
        Only(
            result.TrackedSteps[ValidatorGenerator.DeclaringTypeTrackingName].SelectMany(s => s.Outputs),
            o => string.Equals(((DeclaringTypeDiagnostics)o.Value).Key, key, StringComparison.Ordinal))
            .Reason;

    // The source-output step that reports the diagnostics of the declaring type key names.
    private static IncrementalStepRunReason DeclaringTypeOutputStepReason(GeneratorRunResult result, string key)
    {
        var step = Only(
            result.TrackedOutputSteps.SelectMany(kv => kv.Value),
            s => s.Inputs.Any(i =>
                i.Source.Outputs[i.OutputIndex].Value is DeclaringTypeDiagnostics d
                && string.Equals(d.Key, key, StringComparison.Ordinal)));
        return Only(step.Outputs, _ => true).Reason;
    }

    private static IncrementalStepRunReason ValidatorStepReason(GeneratorRunResult result, string hintName) =>
        Only(
            result.TrackedSteps[ValidatorGenerator.ValidatorTrackingName].SelectMany(s => s.Outputs),
            o => string.Equals(((ValidatorGenerator.GeneratedValidator)o.Value).HintName, hintName, StringComparison.Ordinal))
            .Reason;

    // The source-output step that adds the validator named hintName: the one whose input is that
    // validator's generation step.
    private static IncrementalStepRunReason OutputStepReason(GeneratorRunResult result, string hintName)
    {
        var step = Only(
            result.TrackedOutputSteps.SelectMany(kv => kv.Value),
            s => s.Inputs.Any(i =>
                i.Source.Outputs[i.OutputIndex].Value is ValidatorGenerator.GeneratedValidator v
                && string.Equals(v.HintName, hintName, StringComparison.Ordinal)));
        return Only(step.Outputs, _ => true).Reason;
    }

    /// <summary>The one item matching <paramref name="predicate"/>, asserting there is exactly one.</summary>
    /// <remarks>
    /// Not <c>Assert.Single</c>: HLQ005 flags it by name, a false positive, #212. The same
    /// <c>Assert.True</c> over the count as FailFastDirectReturnTests.SingleFailure.
    /// </remarks>
    private static T Only<T>(IEnumerable<T> items, Func<T, bool> predicate)
    {
        var matches = items.Where(predicate).ToArray();
        Assert.True(matches.Length == 1, $"Expected exactly one match, got {matches.Length}.");
        return matches[0];
    }

    private static void AssertAllSuppressed(GeneratorDriverRunResult result)
    {
        var warnings = result.Diagnostics
            .Where(d => d.Id is "ZV0032" or "ZV0026")
            .ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, d =>
        {
            Assert.True(d.IsSuppressed, $"{d.Id} was not suppressed");
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.NotNull(d.Location.SourceTree);
        });
    }
}
