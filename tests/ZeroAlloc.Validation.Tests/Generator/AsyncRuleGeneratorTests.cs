using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Asynchronous custom rules, <see cref="AsyncValidationAttribute{T}"/>: the generated
/// <c>ValidateAsync</c>, the throwing <c>Validate</c>, their diagnostics, and ZV0034 for options
/// validation. Every test compiles the generated code and asserts it has neither errors nor
/// warnings, since consumers build with warnings as errors.
/// </summary>
public class AsyncRuleGeneratorTests
{
    private const string UniqueDeclaration = """
        public sealed class UniqueAttribute : AsyncValidationAttribute<string?>
        {
            public override System.Threading.Tasks.ValueTask<bool> IsValidAsync(string? value, System.Threading.CancellationToken ct)
                => new(value != "taken");
        }
        """;

    [Fact]
    public void Async_rule_emits_awaiting_ValidateAsync_and_throwing_Validate()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class Request
            {
                [NotEmpty] [Unique] public string? Name { get; init; }
            }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        var src = Generated(result, "RequestValidator.g.cs");
        Assert.Contains("public override global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult> ValidateAsync(global::TestModels.Request instance, global::System.Threading.CancellationToken ct = default)", src, StringComparison.Ordinal);
        Assert.Contains("=> __ValidateAsyncCore(instance, ct);", src, StringComparison.Ordinal);
        Assert.Contains("private static async global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult> __ValidateAsyncCore(", src, StringComparison.Ordinal);
        Assert.Contains("if (!await __Rule_Name_1.IsValidAsync(instance.Name, ct).ConfigureAwait(false))", src, StringComparison.Ordinal);
        Assert.Contains("new global::ZeroAlloc.Validation.Internal.AsyncFailureBuffer(2)", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Internal.FailureBuffer(", src, StringComparison.Ordinal);
        Assert.Contains("=> throw new global::System.NotSupportedException(\"'TestModels.Request' has asynchronous validation rules, which Validate cannot run; call ValidateAsync instead.\");", src, StringComparison.Ordinal);
        Assert.Contains("private static readonly global::TestModels.UniqueAttribute __Rule_Name_1", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_without_async_rules_keeps_the_synchronous_emission()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed class Request
            {
                [NotEmpty] public string? Name { get; init; }
            }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        var src = Generated(result, "RequestValidator.g.cs");
        Assert.DoesNotContain("ValidateAsync", src, StringComparison.Ordinal);
        Assert.DoesNotContain("NotSupportedException", src, StringComparison.Ordinal);
        Assert.Contains("Internal.FailureBuffer(", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Async_rule_honours_When_Unless_and_stop_on_first_failure()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class Request
            {
                [StopOnFirstFailure]
                [NotEmpty]
                [Unique(When = nameof(Check), Unless = nameof(Skip))]
                public string? Name { get; init; }

                public bool Check() => true;
                public bool Skip() => false;
            }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        var src = Generated(result, "RequestValidator.g.cs");
        Assert.Contains(
            "else if (instance.Check() && !instance.Skip() && (!await __Rule_Name_1.IsValidAsync(instance.Name, ct).ConfigureAwait(false)))",
            src, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_stop_on_first_failure_returns_directly_from_the_async_body()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate(StopOnFirstFailure = true)]
            public sealed class Request
            {
                [Unique] public string? First { get; init; }
                [Unique] public string? Second { get; init; }
            }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        var src = Generated(result, "RequestValidator.g.cs");
        Assert.Contains("return new global::ZeroAlloc.Validation.ValidationResult(new global::ZeroAlloc.Validation.ValidationFailure[]", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AsyncFailureBuffer", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_and_error_code_come_from_the_usage_then_RuleMessage()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} '{PropertyValue}' is taken.", ErrorCode = "TAKEN")]
            public sealed class UniqueAttribute : AsyncValidationAttribute<string?>
            {
                public override System.Threading.Tasks.ValueTask<bool> IsValidAsync(string? value, System.Threading.CancellationToken ct)
                    => new(value != "taken");
            }

            [Validate]
            public sealed class Request
            {
                [Unique] public string? Name { get; init; }
                [Unique(Message = "In use.", ErrorCode = "IN_USE", Severity = Severity.Warning)] public string? Alias { get; init; }
            }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0026", StringComparison.Ordinal));
        var src = Generated(result, "RequestValidator.g.cs");
        Assert.Contains("ErrorMessage = $\"Name '{instance.Name ?? \"null\"}' is taken.\", ErrorCode = \"TAKEN\"", src, StringComparison.Ordinal);
        Assert.Contains("ErrorMessage = \"In use.\", ErrorCode = \"IN_USE\", Severity = global::ZeroAlloc.Validation.Severity.Warning", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Async_rule_is_not_reported_as_ZV0020()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class Request { [Unique] public string? Name { get; init; } }
            """;

        var (result, _) = Run(source);

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0020", StringComparison.Ordinal));
    }

    [Fact]
    public void Async_rule_whose_value_type_does_not_match_reports_ZV0021()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class PositiveAttribute : AsyncValidationAttribute<int>
            {
                public override System.Threading.Tasks.ValueTask<bool> IsValidAsync(int value, System.Threading.CancellationToken ct) => new(value > 0);
            }

            [Validate]
            public sealed class Request { [Positive] public string? Name { get; init; } }
            """;

        var (result, _) = Run(source);

        var zv0021 = OnlyDiagnostic(result, "ZV0021");
        Assert.Contains("'PositiveAttribute' validates 'int' but property 'Name' is 'string?'", zv0021.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Inaccessible_async_rule_reports_ZV0023()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed class Request
            {
                [Hidden] public string? Name { get; init; }

                private sealed class HiddenAttribute : AsyncValidationAttribute<string?>
                {
                    public override System.Threading.Tasks.ValueTask<bool> IsValidAsync(string? value, System.Threading.CancellationToken ct) => new(true);
                }
            }
            """;

        var (result, _) = Run(source);

        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0023", StringComparison.Ordinal));
    }

    [Fact]
    public void Parent_of_a_model_with_async_rules_awaits_its_nested_and_collection_validators()
    {
        var source = $$"""
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class Child { [Unique] public string? Name { get; init; } }

            [Validate]
            public sealed class Plain { [NotEmpty] public string? Name { get; init; } }

            [Validate]
            public sealed class Parent
            {
                public Child? Child { get; init; }
                public List<Child>? Children { get; init; }
                public IReadOnlyList<Child>? ReadOnly { get; init; }
                public Child[]? Array { get; init; }
                public IEnumerable<Child>? Sequence { get; init; }
                public Plain? Plain { get; init; }
            }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        var src = Generated(result, "ParentValidator.g.cs");
        Assert.Contains("_buf.AddNested(await _childValidator.ValidateAsync(instance.Child, ct).ConfigureAwait(false), \"Child\");", src, StringComparison.Ordinal);
        Assert.Contains("_buf.AddNested(await _childrenValidator.ValidateAsync(_c0Item, ct).ConfigureAwait(false), \"Children\", _c0Idx);", src, StringComparison.Ordinal);
        // A plain nested model is still called through ValidateAsync, which wraps its Validate.
        Assert.Contains("_buf.AddNested(await _plainValidator.ValidateAsync(instance.Plain, ct).ConfigureAwait(false), \"Plain\");", src, StringComparison.Ordinal);
        // No span or ref local may live across an await.
        Assert.DoesNotContain("CollectionsMarshal", src, StringComparison.Ordinal);
        Assert.DoesNotContain("ref readonly", src, StringComparison.Ordinal);
        Assert.Contains("NotSupportedException", src, StringComparison.Ordinal);

        Assert.DoesNotContain("ValidateAsync", Generated(result, "PlainValidator.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Async_requirement_is_transitive_and_survives_a_cycle()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class Leaf { [Unique] public string? Name { get; init; } }

            [Validate]
            public sealed class Middle { public Leaf? Leaf { get; init; } public Top? Back { get; init; } }

            [Validate]
            public sealed class Top { public Middle? Middle { get; init; } }

            [Validate]
            public sealed class Loop { [NotEmpty] public string? Name { get; init; } public Loop? Next { get; init; } }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        Assert.Contains("__ValidateAsyncCore", Generated(result, "TopValidator.g.cs"), StringComparison.Ordinal);
        Assert.Contains("__ValidateAsyncCore", Generated(result, "MiddleValidator.g.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateAsync", Generated(result, "LoopValidator.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void CustomValidation_methods_of_every_return_type_are_walked_in_the_async_body()
    {
        var source = $$"""
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class Request
            {
                [Unique] public string? Name { get; init; }

                [CustomValidation] public System.ReadOnlySpan<ValidationFailure> Span() => default;
                [CustomValidation] public ValidationFailure[] Array() => System.Array.Empty<ValidationFailure>();
                [CustomValidation] public IEnumerable<ValidationFailure> Sequence() { yield break; }
            }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        var src = Generated(result, "RequestValidator.g.cs");
        Assert.Contains("_buf.AddRange(instance.Span());", src, StringComparison.Ordinal);
        Assert.Contains("foreach (var _cf in instance.Array())", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Async_pipeline_behaviors_wrap_the_async_body()
    {
        var source = $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Pipeline;
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class Request { [Unique] public string? Name { get; init; } }

            [PipelineBehavior(Order = 0, AppliesTo = typeof(Request))]
            public class AuditBehavior : IPipelineBehavior
            {
                public static async ValueTask<ZeroAlloc.Validation.ValidationResult> Handle<TModel>(
                    TModel instance,
                    CancellationToken ct,
                    System.Func<TModel, CancellationToken, ValueTask<ZeroAlloc.Validation.ValidationResult>> next)
                    => await next(instance, ct).ConfigureAwait(false);
            }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        var src = Generated(result, "RequestValidator.g.cs");
        Assert.Contains("AuditBehavior.Handle", src, StringComparison.Ordinal);
        Assert.Contains("return __ValidateAsyncCore(", src, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueTask.FromResult", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_warning_on_an_async_rule_call_is_mirrored_as_ZV0032()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class StrictAttribute : AsyncValidationAttribute<string>
            {
                public override System.Threading.Tasks.ValueTask<bool> IsValidAsync(string value, System.Threading.CancellationToken ct) => new(value.Length > 0);
            }

            [Validate]
            public sealed class Request
            {
                [Strict] [MaybeNull] public string Name { get; init; } = "";
            }
            """;

        var (result, output) = Run(source);

        AssertCleanCompile(output);
        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0032", StringComparison.Ordinal));
        Assert.Contains("#pragma warning disable CS8604", Generated(result, "RequestValidator.g.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("builder.ValidateWithZeroAlloc();")]
    [InlineData("builder?.ValidateWithZeroAlloc();")]
    [InlineData("ZeroAllocOptionsValidationExtensions.ValidateWithZeroAlloc(builder);")]
    public void Options_validation_of_a_model_with_async_rules_reports_ZV0034(string call)
    {
        var source = $$"""
            using Microsoft.Extensions.Options;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class ApiOptions { [Unique] public string? Name { get; set; } }

            public static class Setup
            {
                public static void Configure(OptionsBuilder<ApiOptions> builder)
                {
                    {{call}}
                }
            }
            """;

        var (result, _) = Run(source, withOptions: true);

        var zv0034 = OnlyDiagnostic(result, "ZV0034");
        Assert.Equal(DiagnosticSeverity.Error, zv0034.Severity);
        Assert.Contains("'TestModels.ApiOptions' has asynchronous validation rules", zv0034.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("ValidateWithZeroAlloc", zv0034.Location.SourceTree!.ToString().Substring(zv0034.Location.SourceSpan.Start, zv0034.Location.SourceSpan.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void Options_validation_of_a_model_with_async_nested_rules_reports_ZV0034()
    {
        var source = $$"""
            using Microsoft.Extensions.Options;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class Endpoint { [Unique] public string? Host { get; set; } }

            [Validate]
            public sealed class ApiOptions { public Endpoint? Endpoint { get; set; } }

            public static class Setup
            {
                public static void Configure(OptionsBuilder<ApiOptions> builder) => builder.ValidateWithZeroAlloc();
            }
            """;

        var (result, _) = Run(source, withOptions: true);

        OnlyDiagnostic(result, "ZV0034");
    }

    [Fact]
    public void Options_validation_of_a_model_without_async_rules_reports_nothing()
    {
        var source = """
            using Microsoft.Extensions.Options;
            using ZeroAlloc.Validation;
            using ZeroAlloc.Validation.Options;
            namespace TestModels;

            [Validate]
            public sealed class ApiOptions { [NotEmpty] public string? Name { get; set; } }

            public static class Setup
            {
                public static void Configure(OptionsBuilder<ApiOptions> builder) => builder.ValidateWithZeroAlloc();
            }
            """;

        var (result, output) = Run(source, withOptions: true);

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0034", StringComparison.Ordinal));
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ZV0034_finding_stays_cached_when_an_unrelated_file_is_added()
    {
        var source = $$"""
            using Microsoft.Extensions.Options;
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{UniqueDeclaration}}

            [Validate]
            public sealed class ApiOptions { [Unique] public string? Name { get; set; } }

            public static class Setup
            {
                public static void Configure(OptionsBuilder<ApiOptions> builder) => builder.ValidateWithZeroAlloc();
            }
            """;
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ZeroAlloc.Validation.Generator.ValidatorGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Other; public class Unrelated { }")));

        var run = driver.GetRunResult();
        Assert.True(run.Diagnostics.Count(d => string.Equals(d.Id, "ZV0034", StringComparison.Ordinal)) == 1, "Expected ZV0034 again from the cache.");
        var outputs = run.Results[0].TrackedSteps[ZeroAlloc.Validation.Generator.SyncOnlyOptionsValidation.TrackingName]
            .SelectMany(s => s.Outputs).ToList();
        Assert.True(outputs.Count == 1, $"Expected one ZV0034 finding, got {outputs.Count}.");
        Assert.All(outputs, o => Assert.True(
            o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected a cached or unchanged step, got {o.Reason}"));
    }

    /// <summary>
    /// Runs the validator generator, and with <paramref name="withOptions"/> the options generator
    /// too, over <paramref name="source"/>, against this test host's own assemblies, which include
    /// ZeroAlloc.Pipeline, Microsoft.Extensions.Options and ZeroAlloc.Validation.Options.
    /// </summary>
    private static (GeneratorDriverRunResult Result, Compilation Output) Run(string source, bool withOptions = false)
    {
        var compilation = CreateCompilation(source);

        IIncrementalGenerator[] generators = withOptions
            ? [new ZeroAlloc.Validation.Generator.ValidatorGenerator(), new ZeroAlloc.Validation.Options.Generator.OptionsValidationEmitter()]
            : [new ZeroAlloc.Validation.Generator.ValidatorGenerator()];
        var driver = CSharpGeneratorDriver.Create(generators)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var trusted = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(System.IO.Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        return CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
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

    private static Diagnostic OnlyDiagnostic(GeneratorDriverRunResult result, string id)
    {
        var found = result.Diagnostics.Where(d => string.Equals(d.Id, id, StringComparison.Ordinal)).ToList();
        Assert.True(found.Count == 1, $"Expected one {id}, found {found.Count}.");
        return found[0];
    }

    private static string Generated(GeneratorDriverRunResult result, string hintNameSuffix) =>
        GeneratorTestHelper.GetGeneratedSource(result, $"TestModels.{hintNameSuffix}");
}
