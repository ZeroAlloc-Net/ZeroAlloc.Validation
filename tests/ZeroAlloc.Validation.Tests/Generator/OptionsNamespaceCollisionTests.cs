using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;
using ZeroAlloc.Validation.Options.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Guards issue #193 point 2. Before the fix, <c>ZeroAllocOptionsValidationExtensions</c> (and
/// its internal sibling) was emitted into the global namespace, so any two libraries built with
/// this generator each shipped a public type of that name in the one namespace every consumer
/// shares. Namespacing the generated class next to <see cref="Options.ZeroAllocOptionsValidator{T}"/>
/// does not make the name unique across libraries -- the generator always emits the same class
/// name -- but it keeps the type out of the global namespace, and a consumer that references two
/// independently built libraries, each with its own <c>[Validate]</c> options model, still
/// compiles cleanly: extension method lookup resolves by signature, not by a single canonical
/// declaring-type identity, so two same-named classes in two different referenced assemblies do
/// not collide as long as neither the libraries nor the consumer redeclare the type locally.
/// </summary>
public class OptionsNamespaceCollisionTests
{
    [Fact]
    public void TwoLibraries_EachWithOptionsValidation_ReferencedTogether_ConsumerCompiles()
    {
        var libraryA = BuildLibrary("LibraryA", """
            using ZeroAlloc.Validation;
            namespace LibraryA;
            [Validate] public class FooOptions { [NotEmpty] public string Name { get; set; } = ""; }
            """);
        var libraryB = BuildLibrary("LibraryB", """
            using ZeroAlloc.Validation;
            namespace LibraryB;
            [Validate] public class BarOptions { [NotEmpty] public string Name { get; set; } = ""; }
            """);

        // Each library independently emits a public
        // ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions -- the identical
        // fully-qualified type name in both assemblies. That is unavoidable on its own; the
        // generator always uses the same name. The fix is what happens next.
        Assert.NotNull(libraryA.Compilation.GetTypeByMetadataName(
            "ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions"));
        Assert.NotNull(libraryB.Compilation.GetTypeByMetadataName(
            "ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions"));

        // A consumer referencing both libraries, calling ValidateWithZeroAlloc() for each
        // library's own options type, must still compile -- the two colliding type names never
        // need to be resolved to a single symbol for this to work.
        var consumerSource = """
            using Microsoft.Extensions.DependencyInjection;
            using ZeroAlloc.Validation.Options;
            namespace Consumer;

            public static class Wiring
            {
                public static void Wire(IServiceCollection services)
                {
                    services.AddOptions<LibraryA.FooOptions>().ValidateWithZeroAlloc();
                    services.AddOptions<LibraryB.BarOptions>().ValidateWithZeroAlloc();
                }
            }
            """;

        var consumerCompilation = CSharpCompilation.Create(
            "Consumer",
            [CSharpSyntaxTree.ParseText(consumerSource)],
            TrustedPlatformReferences().Concat([libraryA.Reference, libraryB.Reference]),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(Errors(consumerCompilation));
    }

    /// <summary>
    /// Compiles <paramref name="source"/> as its own assembly, running the validator and options
    /// generators the way a real library build would, and emits it to a real PE image so it can
    /// be referenced by another compilation exactly like a restored NuGet package would be.
    /// </summary>
    private static (Compilation Compilation, MetadataReference Reference) BuildLibrary(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver
            .Create(new ValidatorGenerator(), new OptionsValidationEmitter())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        Assert.Empty(Errors(output));

        using var peStream = new MemoryStream();
        var emitResult = output.Emit(peStream);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics.Select(d => d.ToString())));

        return (output, MetadataReference.CreateFromImage(peStream.ToArray()));
    }

    /// <summary>
    /// This test host's own dependencies, which include ZeroAlloc.Validation,
    /// ZeroAlloc.Validation.Options and the options/dependency-injection assemblies through the
    /// project references.
    /// </summary>
    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

    private static List<string> Errors(Compilation compilation)
    {
        var errors = new List<string>();
        foreach (var d in compilation.GetDiagnostics())
        {
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add($"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        return errors;
    }
}
