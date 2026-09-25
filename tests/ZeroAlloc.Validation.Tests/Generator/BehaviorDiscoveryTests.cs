using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class BehaviorDiscoveryTests
{
    private static readonly MetadataReference[] PipelineReference =
        [MetadataReference.CreateFromFile(typeof(ZeroAlloc.Pipeline.IPipelineBehavior).Assembly.Location)];

    private static Compilation CreateCompilation(string source) =>
        GeneratorTestHelper.CreateCompilation(source, extraReferences: PipelineReference);

    private static GeneratorDriverRunResult RunGenerator(string source) =>
        GeneratorTestHelper.RunGenerator(source, extraReferences: PipelineReference);

    [Fact]
    public void DiscoverAll_FindsSyncBehavior()
    {
        var source = """
            using ZeroAlloc.Pipeline;
            using ZeroAlloc.Validation;

            [PipelineBehavior(Order = 0)]
            public class LoggingBehavior : IPipelineBehavior
            {
                public static ValidationResult Handle<TModel>(
                    TModel instance,
                    System.Func<TModel, ValidationResult> next)
                    => next(instance);
            }
            """;

        var compilation = CreateCompilation(source);
        var (sync, async_) = BehaviorDiscoverer.DiscoverAll(compilation);

        Assert.Collection(sync,
            b => Assert.Contains("LoggingBehavior", b.BehaviorTypeName, System.StringComparison.Ordinal));
        Assert.Empty(async_);
    }

    [Fact]
    public void DiscoverAll_FindsAsyncBehavior()
    {
        var source = """
            using ZeroAlloc.Pipeline;
            using ZeroAlloc.Validation;
            using System.Threading;
            using System.Threading.Tasks;

            [PipelineBehavior(Order = 0)]
            public class CachingBehavior : IPipelineBehavior
            {
                public static async ValueTask<ValidationResult> Handle<TModel>(
                    TModel instance,
                    CancellationToken ct,
                    System.Func<TModel, CancellationToken, ValueTask<ValidationResult>> next)
                    => await next(instance, ct);
            }
            """;

        var compilation = CreateCompilation(source);
        var (sync, async_) = BehaviorDiscoverer.DiscoverAll(compilation);

        Assert.Empty(sync);
        Assert.Collection(async_,
            b => Assert.Contains("CachingBehavior", b.BehaviorTypeName, System.StringComparison.Ordinal));
    }

    [Fact]
    public void ForModel_FiltersGlobalAndPerModel()
    {
        var globalBehavior = new ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo("GlobalB", 0, null, 1);
        var orderBehavior  = new ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo("OrderB",  1, "global::TestModels.Order",  1);
        var personBehavior = new ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo("PersonB", 2, "global::TestModels.Person", 1);

        var allSync = new System.Collections.Generic.List<ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo>
            { globalBehavior, orderBehavior, personBehavior };

        var (orderSync, _) = BehaviorDiscoverer.ForModel(allSync, [], "global::TestModels.Order");

        Assert.Equal(2, orderSync.Count);  // global + order-specific
        Assert.DoesNotContain(orderSync, b => string.Equals(b.BehaviorTypeName, "PersonB", System.StringComparison.Ordinal));
        Assert.Equal(0, orderSync[0].Order);  // GlobalB (Order=0) comes first
        Assert.Equal(1, orderSync[1].Order);  // OrderB (Order=1) comes second
    }

    [Fact]
    public void Generator_WithSyncBehavior_EmitsBehaviorChain_InValidate()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.Pipeline;

            namespace TestModels;

            [Validate]
            public class Order { [NotEmpty] public string Reference { get; set; } = ""; }

            [PipelineBehavior(Order = 0)]
            public class LoggingBehavior : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel instance,
                    System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(instance);
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var generated = result.GeneratedTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("OrderValidator", StringComparison.Ordinal));

        Assert.NotNull(generated);
        Assert.Contains("LoggingBehavior.Handle", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateAsync", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_DuplicateBehaviorOrder_EmitsZV0015()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.Pipeline;

            namespace TestModels;

            [Validate]
            public class Order { [NotEmpty] public string Reference { get; set; } = ""; }

            [PipelineBehavior(Order = 0)]
            public class BehaviorA : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(inst);
            }

            [PipelineBehavior(Order = 0)]
            public class BehaviorB : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(inst);
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0015", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_DuplicateBehaviorOrder_SyncAndAsyncBothOrderZero_EmitsExactlyOneZV0015()
    {
        // ReportDuplicateOrderDiagnostics merges the sync and async lists before checking for
        // Order collisions, since both kinds run in the same per-model behavior chain. A sync
        // behavior and an async behavior sharing Order = 0 must still be reported exactly once,
        // not once per list.
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.Pipeline;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestModels;

            [Validate]
            public class Order { [NotEmpty] public string Reference { get; set; } = ""; }

            [PipelineBehavior(Order = 0)]
            public class SyncBehavior : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(inst);
            }

            [PipelineBehavior(Order = 0)]
            public class AsyncBehavior : IPipelineBehavior
            {
                public static async ValueTask<ZeroAlloc.Validation.ValidationResult> Handle<TModel>(
                    TModel inst, CancellationToken ct,
                    System.Func<TModel, CancellationToken, ValueTask<ZeroAlloc.Validation.ValidationResult>> next)
                    => await next(inst, ct);
            }
            """;

        var result = RunGenerator(source);

        Assert.Equal(1, result.Diagnostics.Count(d => string.Equals(d.Id, "ZV0015", System.StringComparison.Ordinal)));
    }

    [Fact]
    public void Generator_DuplicateBehaviorOrder_NamesANestedModelByItsQualifiedName()
    {
        // Two nested models may share a simple name, so the message must tell them apart.
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.Pipeline;

            namespace TestModels;

            public static class First
            {
                [Validate]
                public class Order { [NotEmpty] public string Reference { get; set; } = ""; }
            }

            [PipelineBehavior(Order = 0)]
            public class BehaviorA : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(inst);
            }

            [PipelineBehavior(Order = 0)]
            public class BehaviorB : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(inst);
            }
            """;

        var result = RunGenerator(source);

        Assert.Equal(1, result.Diagnostics.Count(d => string.Equals(d.Id, "ZV0015", System.StringComparison.Ordinal)));
        var zv0015 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0015", System.StringComparison.Ordinal));
        Assert.Equal(
            "Two behaviors have the same Order value 0 for model 'TestModels.First.Order'. "
            + "'BehaviorA' already uses this Order; each behavior must have a unique Order.",
            zv0015.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Generator_DuplicateBehaviorOrder_ReportsAtSecondBehaviorAttribute()
    {
        // Issue #247: ZV0015 used to report at Location.None, so the user got an error that
        // pointed nowhere. It must now point at the [PipelineBehavior] attribute of the second
        // (colliding) behavior — BehaviorB here — not at the model or the first behavior.
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.Pipeline;

            namespace TestModels;

            [Validate]
            public class Order { [NotEmpty] public string Reference { get; set; } = ""; }

            [PipelineBehavior(Order = 0)]
            public class BehaviorA : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(inst);
            }

            [PipelineBehavior(Order = 0)]
            public class BehaviorB : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(inst);
            }
            """;

        var result = RunGenerator(source);

        Assert.Equal(1, result.Diagnostics.Count(d => string.Equals(d.Id, "ZV0015", System.StringComparison.Ordinal)));
        var zv0015 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0015", System.StringComparison.Ordinal));

        Assert.NotEqual(Location.None, zv0015.Location);
        Assert.True(zv0015.Location.IsInSource);

        // The reported span must fall on BehaviorB's [PipelineBehavior] attribute, not BehaviorA's.
        // AttributeSyntax's own span starts after the '[', at the attribute name.
        var behaviorAAttributeStart = source.IndexOf("[PipelineBehavior(Order = 0)]", StringComparison.Ordinal);
        var behaviorBAttributeStart = source.IndexOf(
            "[PipelineBehavior(Order = 0)]", behaviorAAttributeStart + 1, StringComparison.Ordinal);
        Assert.True(behaviorBAttributeStart > behaviorAAttributeStart);

        Assert.Equal(behaviorBAttributeStart + 1, zv0015.Location.SourceSpan.Start);
    }

    [Fact]
    public void FindBehaviorAttributeLocation_UnresolvableBehavior_ReturnsNull()
    {
        // Compilation.GetTypeByMetadataName returns null both when a name simply does not exist
        // and when it is ambiguous across assemblies (issue #247's "if both are in metadata"
        // case is one way this happens). Either way, FindBehaviorAttributeLocation must not
        // throw and must signal "no location" so the caller falls back, rather than reporting at
        // Location.None as before the fix.
        var compilation = CreateCompilation("namespace TestModels;");
        var unresolvable = new ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo(
            "global::TestModels.NoSuchBehavior", 0, null, 1);

        var location = ZeroAlloc.Validation.Generator.ValidatorGenerator.FindBehaviorAttributeLocation(
            unresolvable, compilation);

        Assert.Null(location);
    }

    [Fact]
    public void ResolveDuplicateOrderLocation_UnresolvableBehavior_FallsBackToValidateAttribute()
    {
        var source = """
            using ZeroAlloc.Validation;

            namespace TestModels;

            [Validate]
            public class Order { [NotEmpty] public string Reference { get; set; } = ""; }
            """;
        var compilation = CreateCompilation(source);
        var classSymbol = compilation.GetTypeByMetadataName("TestModels.Order")!;
        var unresolvable = new ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo(
            "global::TestModels.NoSuchBehavior", 0, null, 1);

        var location = ZeroAlloc.Validation.Generator.ValidatorGenerator.ResolveDuplicateOrderLocation(
            compilation, classSymbol, unresolvable);

        Assert.NotEqual(Location.None, location);
        Assert.True(location.IsInSource);
        // AttributeSyntax's own span starts after the '[', at the attribute name.
        var validateAttributeStart = source.IndexOf("[Validate]", StringComparison.Ordinal);
        Assert.Equal(validateAttributeStart + 1, location.SourceSpan.Start);
    }

    [Theory]
    [InlineData("global::App.LoggingBehavior", "LoggingBehavior")]
    [InlineData("global::App.Outer+Inner", "Inner")]
    [InlineData("NoNamespace", "NoNamespace")]
    public void DescribeBehavior_ReturnsSimpleName(string behaviorTypeName, string expected)
    {
        var behavior = new ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo(behaviorTypeName, 0, null, 1);

        Assert.Equal(expected, ZeroAlloc.Validation.Generator.ValidatorGenerator.DescribeBehavior(behavior));
    }

    [Fact]
    public void Generator_AsyncBehavior_OnNestedModel_GeneratesValidCode()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.Pipeline;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestModels;

            [Validate]
            public class Address { [NotEmpty] public string Street { get; set; } = ""; }

            [Validate]
            public class Customer
            {
                [NotEmpty] public string Name { get; set; } = "";
                [ValidateWith<AddressValidator>] public Address Home { get; set; } = new();
            }

            [PipelineBehavior(Order = 0, AppliesTo = typeof(Customer))]
            public class AuditBehavior : IPipelineBehavior
            {
                public static async ValueTask<ZeroAlloc.Validation.ValidationResult> Handle<TModel>(
                    TModel instance,
                    CancellationToken ct,
                    System.Func<TModel, CancellationToken, ValueTask<ZeroAlloc.Validation.ValidationResult>> next)
                    => await next(instance, ct);
            }
            """;

        var result = RunGenerator(source);

        // No compile errors expected
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var customerValidator = result.GeneratedTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("CustomerValidator", StringComparison.Ordinal));
        Assert.NotNull(customerValidator);
        Assert.Contains("ValidateAsync", customerValidator, System.StringComparison.Ordinal);
        Assert.Contains("ValueTask.FromResult", customerValidator, System.StringComparison.Ordinal);
    }
}
