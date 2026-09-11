using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Which iteration a collection property compiles to. Behaviour is identical either way, so only
/// the emitted shape can show that an interface-typed collection stopped boxing its enumerator.
/// </summary>
public class CollectionIterationEmissionTests
{
    [Theory]
    [InlineData("System.Collections.Generic.IList<Item>")]
    [InlineData("System.Collections.Generic.IReadOnlyList<Item>")]
    public void InterfaceCollection_IsWalkedByIndex(string propertyType)
    {
        var generated = GeneratedSource(ModelWith(propertyType));

        Assert.Contains("for (int _c0Idx = 0; _c0Idx < _c0Src.Count; _c0Idx++)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var _c0Item", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ListCollection_IsWalkedAsSpan()
    {
        var generated = GeneratedSource(ModelWith("System.Collections.Generic.List<Item>"));

        Assert.Contains("CollectionsMarshal.AsSpan(_c0Src)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ArrayCollection_KeepsForeach()
    {
        // foreach over an array already compiles to indexing, so there is nothing to change.
        var generated = GeneratedSource(ModelWith("Item[]"));

        Assert.Contains("foreach (var _c0Item in _c0Src)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionsMarshal", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumerableCollection_FallsBackToForeach()
    {
        // IEnumerable<T> exposes no indexer, so the boxed enumerator is unavoidable here.
        var generated = GeneratedSource(ModelWith("System.Collections.Generic.IEnumerable<Item>"));

        Assert.Contains("foreach (var _c0Item in _c0Src)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("_c0Src.Count", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexedCollection_StillGuardsAgainstNull()
    {
        var generated = GeneratedSource(ModelWith("System.Collections.Generic.IList<Item>"));

        Assert.Contains("instance.Items is not null", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexedCollection_Compiles()
    {
        CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(
                CreateCompilation(ModelWith("System.Collections.Generic.IList<Item>")), out var output, out _);

        var errors = new System.Collections.Generic.List<Diagnostic>();
        foreach (var d in output.GetDiagnostics())
        {
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add(d);
        }

        Assert.Empty(errors);
    }

    private static string ModelWith(string propertyType) => $$"""
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Item
        {
            [NotEmpty]
            public string Code { get; set; } = "";
        }

        [Validate]
        public class Holder
        {
            [NotEmpty]
            public string Name { get; set; } = "";

            public {{propertyType}} Items { get; set; } = null!;
        }
        """;

    private static string GeneratedSource(string source)
    {
        var result = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGenerators(CreateCompilation(source))
            .GetRunResult();

        return string.Join(
            "\n",
            result.Results.SelectMany(r => r.GeneratedSources)
                .Select(s => s.SourceText.ToString())
                .Where(t => t.Contains("HolderValidator", StringComparison.Ordinal)));
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
