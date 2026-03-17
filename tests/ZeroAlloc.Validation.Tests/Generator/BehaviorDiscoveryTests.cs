using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class BehaviorDiscoveryTests
{
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

#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        Assert.Single(sync);
#pragma warning restore HLQ005
        Assert.Empty(async_);
        Assert.Contains("LoggingBehavior", sync[0].BehaviorTypeName, System.StringComparison.Ordinal);
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
#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        Assert.Single(async_);
#pragma warning restore HLQ005
        Assert.Contains("CachingBehavior", async_[0].BehaviorTypeName, System.StringComparison.Ordinal);
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
            .FirstOrDefault(s => s.Contains("OrderValidator"));

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

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        // Ensure ZeroAlloc.Pipeline is loaded into the AppDomain.
        _ = typeof(ZeroAlloc.Pipeline.IPipelineBehavior).Assembly;

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            System.AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(compilation);

        return driver.GetRunResult();
    }

    private static Compilation CreateCompilation(string source)
    {
        // Ensure ZeroAlloc.Pipeline is loaded into the AppDomain so that
        // AppDomain.CurrentDomain.GetAssemblies() includes it as a metadata reference.
        _ = typeof(ZeroAlloc.Pipeline.IPipelineBehavior).Assembly;

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            System.AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
