using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// One shared way for generator tests to build a compilation and run an incremental generator
/// (<see cref="ZeroAlloc.Validation.Generator.ValidatorGenerator"/> by default) against it.
/// </summary>
/// <remarks>
/// Generator test files used to each declare their own copy of this. Some referenced every
/// assembly <c>AppDomain.CurrentDomain.GetAssemblies()</c> happened to have loaded, so the
/// reference set silently depended on what other tests in the same process had already run.
/// Others declared their own <c>ValueObjectAttribute</c> stub source, which could collide
/// (CS0436) with a real <c>ZeroAlloc.ValueObjects</c> reference pulled in by the AppDomain scan.
/// This helper uses an explicit, minimal reference set instead — the BCL, <c>System.Runtime</c>,
/// <c>List&lt;T&gt;</c>'s assembly and ZeroAlloc.Validation itself — with room for a test to add
/// exactly the extra references or extra source files it needs. See #211.
/// </remarks>
internal static class GeneratorTestHelper
{
    /// <summary>
    /// The metadata references every generator test compilation needs at minimum: the BCL, the
    /// library under test (for <see cref="ValidateAttribute"/> and friends), the collection types
    /// the generated code references, and the two facade assemblies (LINQ, regular expressions)
    /// custom validation rule bodies in test sources commonly reach for.
    /// </summary>
    public static IReadOnlyList<MetadataReference> MinimalReferences { get; } = BuildMinimalReferences();

    private static IReadOnlyList<MetadataReference> BuildMinimalReferences()
    {
        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        MetadataReference Runtime(string fileName) =>
            MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, fileName));

        return new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
            Runtime("System.Runtime.dll"),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            Runtime("System.Linq.dll"),
            Runtime("System.Text.RegularExpressions.dll"),
        };
    }

    /// <summary>
    /// Builds a compilation from <paramref name="source"/> and any <paramref name="extraSources"/>,
    /// against <see cref="MinimalReferences"/> plus any <paramref name="extraReferences"/>.
    /// </summary>
    public static CSharpCompilation CreateCompilation(
        string source,
        IEnumerable<string>? extraSources = null,
        IEnumerable<MetadataReference>? extraReferences = null,
        NullableContextOptions nullableContextOptions = NullableContextOptions.Disable)
    {
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source) };
        if (extraSources is not null)
            trees.AddRange(extraSources.Select(s => CSharpSyntaxTree.ParseText(s)));

        var references = new List<MetadataReference>(MinimalReferences);
        if (extraReferences is not null)
            references.AddRange(extraReferences);

        return CSharpCompilation.Create(
            "TestAssembly",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullableContextOptions));
    }

    /// <summary>Runs <paramref name="generator"/> (<see cref="ZeroAlloc.Validation.Generator.ValidatorGenerator"/> by default) and returns its result.</summary>
    public static GeneratorDriverRunResult RunGenerator(
        string source,
        IEnumerable<string>? extraSources = null,
        IEnumerable<MetadataReference>? extraReferences = null,
        NullableContextOptions nullableContextOptions = NullableContextOptions.Disable,
        IIncrementalGenerator? generator = null)
    {
        var compilation = CreateCompilation(source, extraSources, extraReferences, nullableContextOptions);
        var driver = CSharpGeneratorDriver.Create(generator ?? new ZeroAlloc.Validation.Generator.ValidatorGenerator())
            .RunGenerators(compilation);
        return driver.GetRunResult();
    }

    /// <summary>
    /// Runs <paramref name="generator"/> and also returns the compilation with the generated trees
    /// added, for tests that need to compile the result, for example to check for CS diagnostics.
    /// </summary>
    public static (GeneratorDriverRunResult Result, Compilation Output) RunGeneratorAndUpdateCompilation(
        string source,
        IEnumerable<string>? extraSources = null,
        IEnumerable<MetadataReference>? extraReferences = null,
        NullableContextOptions nullableContextOptions = NullableContextOptions.Disable,
        IIncrementalGenerator? generator = null)
    {
        var compilation = CreateCompilation(source, extraSources, extraReferences, nullableContextOptions);
        var driver = CSharpGeneratorDriver.Create(generator ?? new ZeroAlloc.Validation.Generator.ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    /// <summary>
    /// The generated source whose hint name is exactly <paramref name="hintName"/>, for example
    /// <c>TestModels.RequestValidator.g.cs</c>.
    /// </summary>
    /// <remarks>
    /// Matches the full hint name rather than a suffix. Since #207, a nested model's validator
    /// file also ends with, say, <c>RequestValidator.g.cs</c> (as
    /// <c>Ns.Outer_RequestValidator.g.cs</c>), so a suffix match can silently read the wrong file
    /// when a test mixes nested and top-level models that share a name. See #211.
    /// </remarks>
    public static string GetGeneratedSource(GeneratorDriverRunResult result, string hintName) =>
        GetGeneratedTree(result, hintName).ToString();

    /// <summary>
    /// The <see cref="SyntaxTree"/> whose hint name is exactly <paramref name="hintName"/>. For
    /// tests that need the tree itself, for example to match a diagnostic's <c>Location.SourceTree</c>,
    /// rather than just its text. Same full-hint-name matching as <see cref="GetGeneratedSource"/>.
    /// </summary>
    public static SyntaxTree GetGeneratedTree(GeneratorDriverRunResult result, string hintName) =>
        result.Results
            .SelectMany(r => r.GeneratedSources)
            .First(s => string.Equals(s.HintName, hintName, StringComparison.Ordinal))
            .SyntaxTree;
}
