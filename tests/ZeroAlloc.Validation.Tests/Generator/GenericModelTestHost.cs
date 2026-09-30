using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Runs the generators over a source for the generic <c>[Validate]</c> model tests, issue #238,
/// against this test host's own dependencies, dependency injection included, and compiles the
/// result, with the nullable context and XML documentation enabled so CS8xxx, CS1570 and CS1712
/// in generated code fail the tests.
/// </summary>
internal static class GenericModelTestHost
{
    public sealed record Output(
        Compilation Compilation,
        List<Diagnostic> Diagnostics,
        List<(string HintName, string Text)> Generated)
    {
        /// <summary>The ids of the generator diagnostics, sorted.</summary>
        public List<string> Ids()
        {
            var ids = Diagnostics.ConvertAll(d => d.Id);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>The one generator diagnostic, asserting that there is exactly one.</summary>
        public Diagnostic OnlyDiagnostic()
        {
            Assert.Collection(Diagnostics, static _ => { });
            return Diagnostics[0];
        }

        /// <summary>The text of the generated file with exactly <paramref name="hintName"/>.</summary>
        public string Source(string hintName) =>
            Generated.First(g => string.Equals(g.HintName, hintName, StringComparison.Ordinal)).Text;

        /// <summary>Every error and warning of the compilation with the generated files, id and message only.</summary>
        public List<string> CompilerProblems()
        {
            var problems = new List<string>();
            foreach (var d in Compilation.GetDiagnostics())
            {
                if (d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                    problems.Add($"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)} at {d.Location.SourceTree?.FilePath}");
            }
            return problems;
        }

        /// <summary>Asserts that the compilation with the generated files has no error and no warning.</summary>
        public Output Compiles()
        {
            Assert.Empty(CompilerProblems());
            return this;
        }
    }

    private static readonly MetadataReference[] References =
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(System.IO.Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();

    /// <summary>
    /// Runs <paramref name="generators"/>, or the validator generator and the Inject generator
    /// when none are given, over <paramref name="source"/>.
    /// </summary>
    public static Output Run(string source, params IIncrementalGenerator[] generators) =>
        Run([source], generators);

    public static Output Run(IEnumerable<string> sources, params IIncrementalGenerator[] generators)
    {
        if (generators.Length == 0)
            generators = [new ZeroAlloc.Validation.Generator.ValidatorGenerator(), new ZeroAlloc.Validation.Inject.InjectGenerator()];

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Diagnose);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal)
                {
                    // The test sources are not documented; generated code is checked through
                    // CS1570, CS1572 and CS1712, which stay on.
                    ["CS1591"] = ReportDiagnostic.Suppress,
                }));

        var driver = CSharpGeneratorDriver.Create(generators)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generated = new List<(string, string)>();
        foreach (var result in driver.GetRunResult().Results)
        {
            foreach (var file in result.GeneratedSources)
                generated.Add((file.HintName, file.SourceText.ToString()));
        }

        return new Output(output, [.. diagnostics], generated);
    }

    /// <summary>The source text a diagnostic points at.</summary>
    public static string SourceAt(Diagnostic diagnostic)
    {
        var tree = diagnostic.Location.SourceTree;
        Assert.NotNull(tree);
        return tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }
}
