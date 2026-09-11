using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Shape of the emitted fail-fast validator. The behavioural guarantees live in
/// <c>FailFastDirectReturnTests</c>; these pin that the allocation actually went away,
/// which behaviour alone cannot show.
/// </summary>
public class FailFastEmissionTests
{
    private const string SingleRuleModel = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate(StopOnFirstFailure = true)]
        public sealed class Request
        {
            [NotEmpty]
            public string? PlayerId { get; init; }
        }
        """;

    [Fact]
    public void SingleRuleFailFast_EmitsNoScratchBuffer()
    {
        var source = GeneratedSource(SingleRuleModel);

        Assert.DoesNotContain("_buf", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Array.Copy", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleRuleFailFast_ReturnsResultDirectly()
    {
        var source = GeneratedSource(SingleRuleModel);

        Assert.Contains(
            "return new global::ZeroAlloc.Validation.ValidationResult(new global::ZeroAlloc.Validation.ValidationFailure[]",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PerPropertyStopFailFast_EmitsNoScratchBuffer()
    {
        var source = GeneratedSource("""
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate(StopOnFirstFailure = true)]
            public sealed class Request
            {
                [StopOnFirstFailure]
                [NotEmpty]
                [MinLength(3)]
                public string Region { get; init; } = "";
            }
            """);

        Assert.DoesNotContain("_buf", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiRuleWithoutPerPropertyStop_KeepsBuffer()
    {
        // Two rules and no per-property stop means two failures are possible,
        // so this group must keep the buffer.
        var source = GeneratedSource("""
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate(StopOnFirstFailure = true)]
            public sealed class Request
            {
                [NotEmpty]
                [MinLength(3)]
                public string Region { get; init; } = "";
            }
            """);

        Assert.Contains("_buf", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutModelLevelFailFast_KeepsBuffer()
    {
        // The optimization is only sound under model-level fail-fast, where reaching a
        // group proves no earlier failure survived.
        var source = GeneratedSource("""
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed class Request
            {
                [NotEmpty]
                public string? PlayerId { get; init; }
            }
            """);

        Assert.Contains("_buf", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FailFastValidator_Compiles()
    {
        CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(CreateCompilation(SingleRuleModel), out var output, out _);

        var errors = new System.Collections.Generic.List<Diagnostic>();
        foreach (var d in output.GetDiagnostics())
        {
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add(d);
        }

        Assert.Empty(errors);
    }

    private static string GeneratedSource(string source)
    {
        var result = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGenerators(CreateCompilation(source))
            .GetRunResult();

        return string.Join(
            "\n",
            result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
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
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
