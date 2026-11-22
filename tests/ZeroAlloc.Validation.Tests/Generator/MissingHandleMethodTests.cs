using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Issue #410: a pipeline behavior takes the validator as its state, so its <c>Handle</c> has the
/// type parameters <c>TModel</c> and <c>TState</c>. One still written as <c>Handle&lt;TModel&gt;</c>
/// is reported as ZV0041 and left out of the generated validator, instead of failing inside it.
/// </summary>
public class MissingHandleMethodTests
{
    private const string Id = "ZV0041";

    private static readonly MetadataReference[] PipelineReference =
        [MetadataReference.CreateFromFile(typeof(ZeroAlloc.Pipeline.IPipelineBehavior).Assembly.Location)];

    private const string Model = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Validation;
        using ZeroAlloc.Pipeline;

        namespace TestModels;

        [Validate]
        public class Order { [NotEmpty] public string Reference { get; set; } = ""; }

        """;

    private static GeneratorDriverRunResult Run(string behavior) =>
        GeneratorTestHelper.RunGenerator(Model + behavior, null, extraReferences: PipelineReference);

    private static ImmutableArray<Diagnostic> Reported(GeneratorDriverRunResult result) =>
        [.. result.Diagnostics.Where(d => string.Equals(d.Id, Id, StringComparison.Ordinal))];

    private static string Validator(GeneratorDriverRunResult result) =>
        GeneratorTestHelper.GetGeneratedSource(result, "TestModels.OrderValidator.g.cs");

    public static TheoryData<string, string> OldShapes => new()
    {
        {
            "OldSync",
            """
            [PipelineBehavior(Order = 0)]
            public class OldSync : IPipelineBehavior
            {
                public static ValidationResult Handle<TModel>(TModel instance, Func<TModel, ValidationResult> next)
                    => next(instance);
            }
            """
        },
        {
            "OldAsync",
            """
            [PipelineBehavior(Order = 0)]
            public class OldAsync : IPipelineBehavior
            {
                public static ValueTask<ValidationResult> Handle<TModel>(
                    TModel instance, CancellationToken ct,
                    Func<TModel, CancellationToken, ValueTask<ValidationResult>> next)
                    => next(instance, ct);
            }
            """
        },
        {
            "NoHandle",
            """
            [PipelineBehavior(Order = 0)]
            public class NoHandle : IPipelineBehavior { }
            """
        },
    };

    [Theory]
    [MemberData(nameof(OldShapes))]
    public void A_behavior_on_the_old_shape_is_reported_and_not_called(string name, string behavior)
    {
        var result = Run(behavior);

        var diagnostic = Assert.Single(Reported(result));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith($"'{name}' has no Handle method that matches a validation pipeline behavior", message, StringComparison.Ordinal);
        Assert.Contains("Handle<TModel, TState>(TModel instance, TState state, Func<TModel, TState, ValidationResult> next)", message, StringComparison.Ordinal);
        Assert.Contains("ValueTask<ValidationResult>> next)", message, StringComparison.Ordinal);
        Assert.Contains("a Handle<TModel> without the state is the shape of Validation 2", message, StringComparison.Ordinal);

        // At the [PipelineBehavior] attribute, which AttributeSyntax starts after the '['.
        var source = Model + behavior;
        Assert.Equal(source.IndexOf("[PipelineBehavior", StringComparison.Ordinal) + 1, diagnostic.Location.SourceSpan.Start);

        // Left out of the validator, so the user sees ZV0041 and no second error in generated code.
        Assert.DoesNotContain(name, Validator(result), StringComparison.Ordinal);
    }

    [Fact]
    public void A_behavior_with_the_validation_shape_is_not_reported_and_is_called()
    {
        var result = Run("""
            [PipelineBehavior(Order = 0)]
            public class SyncOk : IPipelineBehavior
            {
                public static ValidationResult Handle<TModel, TState>(
                    TModel instance, TState state, Func<TModel, TState, ValidationResult> next)
                    => next(instance, state);
            }

            [PipelineBehavior(Order = 1)]
            public class AsyncOk : IPipelineBehavior
            {
                public static ValueTask<ValidationResult> Handle<TModel, TState>(
                    TModel instance, TState state, CancellationToken ct,
                    Func<TModel, TState, CancellationToken, ValueTask<ValidationResult>> next)
                    => next(instance, state, ct);
            }
            """);

        Assert.Empty(Reported(result));
        var source = Validator(result);
        Assert.Contains("SyncOk.Handle<", source, StringComparison.Ordinal);
        Assert.Contains("AsyncOk.Handle<", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_broken_behavior_is_left_out_when_a_valid_one_shares_the_model()
    {
        var result = Run("""
            [PipelineBehavior(Order = 0)]
            public class OldSync : IPipelineBehavior
            {
                public static ValidationResult Handle<TModel>(TModel instance, Func<TModel, ValidationResult> next)
                    => next(instance);
            }

            [PipelineBehavior(Order = 1)]
            public class SyncOk : IPipelineBehavior
            {
                public static ValidationResult Handle<TModel, TState>(
                    TModel instance, TState state, Func<TModel, TState, ValidationResult> next)
                    => next(instance, state);
            }
            """);

        Assert.Single(Reported(result));
        var source = Validator(result);
        Assert.DoesNotContain("OldSync", source, StringComparison.Ordinal);
        Assert.Contains("SyncOk.Handle<", source, StringComparison.Ordinal);
    }
}
