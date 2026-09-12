using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Rule attributes are <c>AllowMultiple</c> because repeating a check with different arguments is
/// meaningful. Repeating one with identical arguments is not — the rule runs twice and reports the
/// same failure twice — so only that case is flagged.
/// </summary>
public class DuplicateAttributeDiagnosticTests
{
    [Fact]
    public void IdenticalAttribute_Twice_Fires_ZV0018()
    {
        var diagnostics = RunGenerator("""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [NotEmpty]
                [NotEmpty]
                public string? Tenant { get; init; }
            }
            """);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZV0018", StringComparison.Ordinal));
    }

    [Fact]
    public void IdenticalAttribute_WithSameArguments_Fires_ZV0018()
    {
        var diagnostics = RunGenerator("""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [MinLength(3)]
                [MinLength(3)]
                public string Tenant { get; init; } = "";
            }
            """);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZV0018", StringComparison.Ordinal));
    }

    [Fact]
    public void SameAttribute_DifferentArguments_DoesNotFire()
    {
        // Two different length bounds are a legitimate pair.
        var diagnostics = RunGenerator("""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [MinLength(3)]
                [MinLength(5)]
                public string Tenant { get; init; } = "";
            }
            """);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZV0018", StringComparison.Ordinal));
    }

    [Fact]
    public void SameAttribute_DifferentPredicates_DoesNotFire()
    {
        // The case AllowMultiple exists for.
        var diagnostics = RunGenerator("""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [Must(nameof(IsShort))]
                [Must(nameof(IsLower))]
                public string Tenant { get; init; } = "";

                public bool IsShort(string v) => v.Length < 10;
                public bool IsLower(string v) => v == v.ToLowerInvariant();
            }
            """);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZV0018", StringComparison.Ordinal));
    }

    [Fact]
    public void SameAttribute_DifferentNamedArguments_DoesNotFire()
    {
        var diagnostics = RunGenerator("""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [NotEmpty(ErrorCode = "A")]
                [NotEmpty(ErrorCode = "B")]
                public string? Tenant { get; init; }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZV0018", StringComparison.Ordinal));
    }

    [Fact]
    public void SameAttribute_NamedArgumentsReordered_Fires_ZV0018()
    {
        // Named arguments are order-independent, so these two are the same rule.
        var diagnostics = RunGenerator("""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [NotEmpty(ErrorCode = "A", Message = "nope")]
                [NotEmpty(Message = "nope", ErrorCode = "A")]
                public string? Tenant { get; init; }
            }
            """);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZV0018", StringComparison.Ordinal));
    }

    [Fact]
    public void DifferentAttributes_DoNotFire()
    {
        var diagnostics = RunGenerator("""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [NotEmpty]
                [MinLength(3)]
                public string Tenant { get; init; } = "";
            }
            """);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZV0018", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateOnOneProperty_DoesNotImplicateAnother()
    {
        var diagnostics = RunGenerator("""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [NotEmpty]
                [NotEmpty]
                public string? Tenant { get; init; }

                [NotEmpty]
                public string? User { get; init; }
            }
            """);

        var zv0018 = new System.Collections.Generic.List<Diagnostic>();
        foreach (var d in diagnostics)
        {
            if (string.Equals(d.Id, "ZV0018", StringComparison.Ordinal))
                zv0018.Add(d);
        }

#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        var diagnostic = Assert.Single(zv0018);
#pragma warning restore HLQ005
        Assert.Contains("Tenant", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static System.Collections.Immutable.ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var trusted = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(System.IO.Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGenerators(compilation)
            .GetRunResult()
            .Diagnostics;
    }
}
