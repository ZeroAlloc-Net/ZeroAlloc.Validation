using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Pipeline behaviors on a model whose validation reads nested validators, issue #294. The body
/// the behaviors wrap reads the validator's nested-validator fields, so the chain's lambdas cannot
/// be <c>static</c>; they were, and the generated validator failed with CS8821. A model without
/// nested validators keeps static lambdas, which capture nothing.
/// </summary>
public class PipelineBehaviorNestedValidatorTests
{
    private const string SyncBehavior = """
        [PipelineBehavior(Order = 0, AppliesTo = typeof(Request))]
        public class AuditBehavior : IPipelineBehavior
        {
            public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                TModel instance, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                => next(instance);
        }
        """;

    private const string AsyncBehavior = """
        [PipelineBehavior(Order = 1, AppliesTo = typeof(Request))]
        public class AsyncAuditBehavior : IPipelineBehavior
        {
            public static async ValueTask<ZeroAlloc.Validation.ValidationResult> Handle<TModel>(TModel instance, CancellationToken ct,
                Func<TModel, CancellationToken, ValueTask<ZeroAlloc.Validation.ValidationResult>> next)
                => await next(instance, ct).ConfigureAwait(false);
        }
        """;

    private const string UniqueDeclaration = """
        public sealed class UniqueAttribute : AsyncValidationAttribute<string?>
        {
            public override ValueTask<bool> IsValidAsync(string? value, CancellationToken ct)
                => new(value != "taken");
        }
        """;

    private static string Source(string requestMembers, string behaviors, string extra = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Pipeline;
        using ZeroAlloc.Validation;
        namespace TestModels;

        {{extra}}

        [Validate] public sealed class Child { [NotEmpty] public string? Name { get; init; } }

        [Validate]
        public sealed class Request
        {
            {{requestMembers}}
        }

        {{behaviors}}
        """;

    public static TheoryData<string, string> BehaviorsOnNestedModels => new()
    {
        // The shape reported in #294: an asynchronous behavior on a synchronous model.
        { "public Child? Child { get; init; }", AsyncBehavior },
        { "public Child? Child { get; init; }", SyncBehavior },
        { "public Child? Child { get; init; }", SyncBehavior + AsyncBehavior },
        { "public List<Child>? Children { get; init; }", AsyncBehavior },
        { "public List<Child>? Children { get; init; }", SyncBehavior },
    };

    [Theory]
    [MemberData(nameof(BehaviorsOnNestedModels))]
    public void Behaviors_on_a_model_with_nested_validators_compile(string nestedMember, string behaviors)
    {
        var (result, output) = Run(Source("[NotEmpty] public string? Name { get; init; }\n    " + nestedMember, behaviors));

        AssertCleanCompile(output);
        var src = Generated(result);
        Assert.Contains("AuditBehavior.Handle", src, StringComparison.Ordinal);
        Assert.DoesNotContain("static (", src, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SyncBehavior)]
    [InlineData(AsyncBehavior)]
    public void Behaviors_on_a_model_with_a_ValidateWith_validator_compile(string behavior)
    {
        // A [ValidateWith] validator is constructor-injected into an instance field, as a nested one is.
        const string checker = """
            public class Money { public decimal Amount { get; set; } }
            public sealed class MoneyChecker : ValidatorFor<Money>
            {
                public override ZeroAlloc.Validation.ValidationResult Validate(Money instance) =>
                    new ZeroAlloc.Validation.ValidationResult(Array.Empty<ValidationFailure>());
            }
            """;
        var (result, output) = Run(Source(
            "[NotEmpty] public string? Name { get; init; }\n    [ValidateWith(typeof(MoneyChecker))] public Money Total { get; set; } = new();",
            behavior,
            checker));

        AssertCleanCompile(output);
        Assert.DoesNotContain("static (", Generated(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Async_behavior_on_a_model_with_async_rules_and_nested_validators_compiles()
    {
        // The asynchronous validation of #202: its core method reads the nested validators, so it
        // is an instance method, and the chain calling it cannot be static either.
        var (result, output) = Run(Source(
            "[NotEmpty] [Unique] public string? Name { get; init; }\n    public Child? Child { get; init; }",
            AsyncBehavior,
            UniqueDeclaration));

        AssertCleanCompile(output);
        var src = Generated(result);
        Assert.Contains("private async global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult> __ValidateAsyncCore(", src, StringComparison.Ordinal);
        Assert.Contains("return __ValidateAsyncCore(", src, StringComparison.Ordinal);
        Assert.DoesNotContain("static (", src, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SyncBehavior)]
    [InlineData(AsyncBehavior)]
    public void Behaviors_on_a_model_without_nested_validators_keep_static_lambdas(string behavior)
    {
        var (result, output) = Run(Source("[NotEmpty] public string? Name { get; init; }", behavior));

        AssertCleanCompile(output);
        Assert.Contains("static (", Generated(result), StringComparison.Ordinal);
    }

    private static (GeneratorDriverRunResult Result, Compilation Output) Run(string source)
    {
        var trusted = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(System.IO.Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new ZeroAlloc.Validation.Generator.ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    /// <summary>No error anywhere, and no warning inside generated code, where a consumer could not fix it.</summary>
    private static void AssertCleanCompile(Compilation output)
    {
        var problems = new List<string>();
        foreach (var d in output.GetDiagnostics())
        {
            var generated = d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true;
            if (d.Severity == DiagnosticSeverity.Error || (generated && d.Severity == DiagnosticSeverity.Warning))
                problems.Add(d.ToString());
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static string Generated(GeneratorDriverRunResult result) =>
        GeneratorTestHelper.GetGeneratedSource(result, "TestModels.RequestValidator.g.cs");
}
