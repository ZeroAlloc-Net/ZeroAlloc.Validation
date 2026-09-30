using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Issue #288: a <c>[PipelineBehavior]</c> type that does not implement <c>IPipelineBehavior</c>
/// never joins a validator's pipeline. It used to be dropped without a word; it is now reported
/// as ZV0035, a warning.
/// </summary>
public class MissingPipelineBehaviorInterfaceTests
{
    private const string Id = "ZV0035";

    private static readonly MetadataReference[] PipelineReference =
        [MetadataReference.CreateFromFile(typeof(ZeroAlloc.Pipeline.IPipelineBehavior).Assembly.Location)];

    private const string Model = """
        using ZeroAlloc.Validation;
        using ZeroAlloc.Pipeline;

        namespace TestModels;

        [Validate]
        public class Order { [NotEmpty] public string Reference { get; set; } = ""; }

        """;

    private const string HandleMethod = """
            public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                => next(inst);
        """;

    private static GeneratorDriverRunResult RunGenerator(string source, IEnumerable<string>? extraSources = null) =>
        GeneratorTestHelper.RunGenerator(source, extraSources, extraReferences: PipelineReference);

    private static ImmutableArray<Diagnostic> WithId(GeneratorDriverRunResult result, string id) =>
        [.. result.Diagnostics.Where(d => string.Equals(d.Id, id, StringComparison.Ordinal))];

    private static Diagnostic OnlyOne(ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.Collection(diagnostics, static _ => { });
        return diagnostics[0];
    }

    private static string OrderValidator(GeneratorDriverRunResult result) =>
        GeneratorTestHelper.GetGeneratedSource(result, "TestModels.OrderValidator.g.cs");

    [Fact]
    public void Static_class_is_reported_with_the_static_fix()
    {
        var source = Model + $$"""
            [PipelineBehavior(Order = 0)]
            public static class StaticLogging
            {
            {{HandleMethod}}
            }
            """;

        var result = RunGenerator(source);

        var diagnostic = OnlyOne(WithId(result, Id));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "'StaticLogging' has [PipelineBehavior] but does not implement IPipelineBehavior, so it never "
            + "runs in a validator's pipeline; make the class non-static and implement IPipelineBehavior",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        // At the [PipelineBehavior] attribute, as ZV0015; AttributeSyntax starts after the '['.
        Assert.Equal(source.IndexOf("[PipelineBehavior", StringComparison.Ordinal) + 1, diagnostic.Location.SourceSpan.Start);
        Assert.DoesNotContain("StaticLogging", OrderValidator(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Class_without_the_interface_is_reported_with_the_interface_fix()
    {
        var source = Model + $$"""
            [PipelineBehavior(Order = 0)]
            public class ForgotInterface
            {
            {{HandleMethod}}
            }
            """;

        var result = RunGenerator(source);

        var diagnostic = OnlyOne(WithId(result, Id));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "'ForgotInterface' has [PipelineBehavior] but does not implement IPipelineBehavior, so it never "
            + "runs in a validator's pipeline; implement IPipelineBehavior",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(source.IndexOf("[PipelineBehavior", StringComparison.Ordinal) + 1, diagnostic.Location.SourceSpan.Start);
        Assert.DoesNotContain("ForgotInterface", OrderValidator(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_behavior_is_not_reported()
    {
        var source = Model + $$"""
            [PipelineBehavior(Order = 0)]
            public class LoggingBehavior : IPipelineBehavior
            {
            {{HandleMethod}}
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("LoggingBehavior.Handle", OrderValidator(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Reported_once_however_many_models_there_are()
    {
        // The behavior would apply to every model, but it is one mistake in one place.
        var source = Model + $$"""
            [Validate]
            public class Customer { [NotEmpty] public string Name { get; set; } = ""; }

            [PipelineBehavior(Order = 0)]
            public class ForgotInterface
            {
            {{HandleMethod}}
            }
            """;

        OnlyOne(WithId(RunGenerator(source), Id));
    }

    [Fact]
    public void Reported_without_any_validate_model()
    {
        var source = $$"""
            using ZeroAlloc.Pipeline;

            namespace TestModels;

            [PipelineBehavior(Order = 0)]
            public static class StaticLogging
            {
                public static T Handle<T>(T inst, System.Func<T, T> next) => next(inst);
            }
            """;

        OnlyOne(WithId(RunGenerator(source), Id));
    }

    [Fact]
    public void Nested_type_is_reported_at_its_attribute()
    {
        var source = Model + $$"""
            public static class Behaviors
            {
                [PipelineBehavior(Order = 0)]
                public static class StaticLogging
                {
                {{HandleMethod}}
                }
            }
            """;

        var result = RunGenerator(source);

        var diagnostic = OnlyOne(WithId(result, Id));
        Assert.Equal(source.IndexOf("[PipelineBehavior", StringComparison.Ordinal) + 1, diagnostic.Location.SourceSpan.Start);
    }

    [Fact]
    public void Nested_valid_behavior_runs()
    {
        // The discoverer names a nested type Outer.Inner, which is not its metadata name
        // Outer+Inner, so re-resolving it by that name used to fail and the behavior was dropped.
        var source = Model + $$"""
            public static class Behaviors
            {
                [PipelineBehavior(Order = 0)]
                public class Logging : IPipelineBehavior
                {
                {{HandleMethod}}
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("Behaviors.Logging.Handle", OrderValidator(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Attribute_subclass_is_reported_at_its_attribute()
    {
        var source = Model + $$"""
            public sealed class AuditBehaviorAttribute : PipelineBehaviorAttribute { }

            [AuditBehavior(Order = 0)]
            public static class StaticAudit
            {
            {{HandleMethod}}
            }
            """;

        var result = RunGenerator(source);

        var diagnostic = OnlyOne(WithId(result, Id));
        Assert.Equal(source.IndexOf("[AuditBehavior", StringComparison.Ordinal) + 1, diagnostic.Location.SourceSpan.Start);
    }

    [Fact]
    public void Pragma_at_the_attribute_suppresses_it()
    {
        var source = Model + $$"""
            #pragma warning disable ZV0035 // kept for a later migration
            [PipelineBehavior(Order = 0)]
            #pragma warning restore ZV0035
            public static class StaticLogging
            {
            {{HandleMethod}}
            }
            """;

        var diagnostic = OnlyOne(WithId(RunGenerator(source), Id));
        Assert.True(diagnostic.IsSuppressed);
    }

    [Fact]
    public void NoWarn_suppresses_it()
    {
        var source = Model + $$"""
            [PipelineBehavior(Order = 0)]
            public static class StaticLogging
            {
            {{HandleMethod}}
            }
            """;
        var compilation = GeneratorTestHelper.CreateCompilation(source, extraReferences: PipelineReference);
        compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
            new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal) { [Id] = ReportDiagnostic.Suppress }));

        var result = CSharpGeneratorDriver.Create(new ValidatorGenerator()).RunGenerators(compilation).GetRunResult();

        Assert.DoesNotContain(WithId(result, Id), d => !d.IsSuppressed);
    }

    [Fact]
    public void Partial_behavior_with_an_attribute_on_a_second_part_runs_once_without_ZV0015()
    {
        // Before ZeroAlloc.Pipeline 1.3.0, discovery returned a partial behavior once per part that
        // had any attribute list, so the behavior was emitted twice and ZV0015 reported it as
        // colliding with itself.
        var source = Model + $$"""
            [PipelineBehavior(Order = 0)]
            public partial class LoggingBehavior : IPipelineBehavior
            {
            {{HandleMethod}}
            }
            """;
        var secondPart = """
            namespace TestModels;

            [System.Serializable]
            public partial class LoggingBehavior { }
            """;

        var result = RunGenerator(source, [secondPart]);

        Assert.Empty(result.Diagnostics);
        var validator = OrderValidator(result);
        Assert.Equal(1, CountOccurrences(validator, "LoggingBehavior.Handle"));

        var (sync, async_) = BehaviorDiscoverer.DiscoverAll(
            GeneratorTestHelper.CreateCompilation(source, [secondPart], PipelineReference));
        Assert.Collection(sync, b => Assert.EndsWith(".LoggingBehavior", b.BehaviorTypeName, StringComparison.Ordinal));
        Assert.Empty(async_);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
