using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Guards issue #55 and the documentation emitted to close it.
/// <para>
/// The &lt;auto-generated&gt; header suppresses analyzer diagnostics but not compiler ones, so
/// CS1591 fired on every generated public member for consumers who enable
/// GenerateDocumentationFile — an error for anyone who also sets TreatWarningsAsErrors.
/// </para>
/// <para>
/// The fix documents the generated members rather than emitting
/// <c>#pragma warning disable CS1591</c>. That choice is what these tests protect: without a
/// suppression, any publicly visible member added to a generator in future breaks consumers
/// unless it is documented too, and only a test in this repository can catch that. The XML
/// assertions cover the second half of the choice — real documentation reaches the consumer's
/// own documentation file, which a pragma would not achieve.
/// </para>
/// </summary>
public class GeneratedCodeDocumentationTests
{
    // CS1591 missing comment, CS1570 malformed XML, CS1571 duplicate param, CS1572 unknown param,
    // CS1573 missing param, CS1574/CS1580/CS1581 unresolvable cref, CS1584 malformed cref.
    // A generator emitting a bad cref or a raw angle bracket would break consumers exactly like
    // the original bug did, so the whole family is asserted, not CS1591 alone.
    private static readonly HashSet<string> s_docDiagnosticIds = new(StringComparer.Ordinal)
    {
        "CS1570", "CS1571", "CS1572", "CS1573", "CS1574", "CS1580", "CS1581", "CS1584", "CS1591",
    };

    private const string ValidateSource = """
        using ZeroAlloc.Validation;
        namespace MyApp;

        /// <summary>A documented consumer type.</summary>
        [Validate]
        public class ChunkingOptions
        {
            /// <summary>The connection string.</summary>
            [NotEmpty] public string ConnectionString { get; set; } = "";
        }
        """;

    // A property whose type also carries [Validate] makes the generator emit the constructor
    // taking the nested validators. It is publicly visible and so needs documentation of its
    // own — including a <param> for every parameter, or CS1573 replaces CS1591.
    private const string NestedValidateSource = """
        using ZeroAlloc.Validation;
        namespace MyApp;

        /// <summary>A documented nested type.</summary>
        [Validate]
        public class Address
        {
            /// <summary>The city.</summary>
            [NotEmpty] public string City { get; set; } = "";
        }

        /// <summary>A documented outer type.</summary>
        [Validate]
        public class Customer
        {
            /// <summary>The address.</summary>
            public Address Home { get; set; } = new();
        }
        """;

    [Fact]
    public void Validator_GeneratedCode_RaisesNoDocumentationDiagnostics()
        => AssertNoDocumentationDiagnostics(new ValidatorGenerator(), ValidateSource);

    [Fact]
    public void ValidatorWithNestedMember_GeneratedCode_RaisesNoDocumentationDiagnostics()
        => AssertNoDocumentationDiagnostics(new ValidatorGenerator(), NestedValidateSource);

    [Fact]
    public void InjectRegistration_GeneratedCode_RaisesNoDocumentationDiagnostics()
        => AssertNoDocumentationDiagnostics(new Validation.Inject.InjectGenerator(), ValidateSource);

    [Fact]
    public void OptionsValidation_GeneratedCode_RaisesNoDocumentationDiagnostics()
        => AssertNoDocumentationDiagnostics(new Validation.Options.Generator.OptionsValidationEmitter(), ValidateSource);

    [Fact]
    public void AspNetCoreExtensions_GeneratedCode_RaisesNoDocumentationDiagnostics()
        => AssertNoDocumentationDiagnostics(new Validation.AspNetCore.Generator.AspNetCoreFilterEmitter(), ValidateSource);

    [Theory]
    // The class declaration and Validate were the six diagnostics reported in issue #55.
    [InlineData("T:MyApp.ChunkingOptionsValidator")]
    [InlineData("M:MyApp.ChunkingOptionsValidator.Validate(MyApp.ChunkingOptions)")]
    public void Validator_GeneratedMembers_AppearInXmlDocumentation(string memberId)
        => Assert.Contains(memberId, EmitXmlDocumentation(new ValidatorGenerator(), ValidateSource), StringComparison.Ordinal);

    [Fact]
    public void NestedValidatorConstructor_DocumentsParameterByMemberName()
    {
        var xml = EmitXmlDocumentation(new ValidatorGenerator(), NestedValidateSource);

        Assert.Contains("M:MyApp.CustomerValidator.#ctor(MyApp.AddressValidator)", xml, StringComparison.Ordinal);
        // The parameter is homeValidator; the documentation should name the Home property
        // rather than fall back to a generic description.
        Assert.Contains("""<param name="homeValidator">""", xml, StringComparison.Ordinal);
        Assert.Contains("<c>Home</c>", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserts that nothing the generator emitted raises a documentation diagnostic.
    /// </summary>
    /// <remarks>
    /// Deliberately does not require the compilation to succeed. The ASP.NET Core and Options
    /// generators emit code against packages this test project does not reference, but
    /// documentation diagnostics are raised against declarations and do not depend on those
    /// references resolving — so the assertion stays valid without dragging in the framework.
    /// </remarks>
    private static void AssertNoDocumentationDiagnostics(IIncrementalGenerator generator, string source)
    {
        var (compilation, sourceTree) = Run(generator, source);

        var docDiagnostics = new List<string>();
        foreach (var d in compilation.GetDiagnostics())
        {
            if (!s_docDiagnosticIds.Contains(d.Id))
                continue;

            // Only generator output is our concern, never the consumer's own source.
            var tree = d.Location.SourceTree;
            if (tree is not null && tree != sourceTree)
                docDiagnostics.Add(d.ToString());
        }

        Assert.Empty(docDiagnostics);
    }

    /// <summary>
    /// Emits the compilation and returns the XML documentation file produced alongside it.
    /// </summary>
    private static string EmitXmlDocumentation(IIncrementalGenerator generator, string source)
    {
        var (compilation, _) = Run(generator, source);

        using var peStream  = new System.IO.MemoryStream();
        using var xmlStream = new System.IO.MemoryStream();
        var emitResult = compilation.Emit(peStream, xmlDocumentationStream: xmlStream);

        // A failed emit yields a misleading empty documentation file, so surface the reason.
        Assert.True(
            emitResult.Success,
            "Generated code failed to compile: " + string.Join("; ", DescribeErrors(emitResult.Diagnostics)));

        return System.Text.Encoding.UTF8.GetString(xmlStream.ToArray());
    }

    private static List<string> DescribeErrors(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = new List<string>();
        foreach (var d in diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add(d.ToString());
        }
        return errors;
    }

    private static (Compilation Compilation, SyntaxTree SourceTree) Run(IIncrementalGenerator generator, string source)
    {
        // DocumentationMode.Diagnose is the parse-options equivalent of
        // <GenerateDocumentationFile>true</GenerateDocumentationFile>.
        var parseOptions = new CSharpParseOptions(documentationMode: DocumentationMode.Diagnose);
        var sourceTree   = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Source.cs");

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "DocGenTest",
            [sourceTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // The driver needs the same parse options, otherwise generated trees are parsed with
        // DocumentationMode.Parse and documentation diagnostics are never evaluated against them.
        CSharpGeneratorDriver
            .Create([generator.AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return (outputCompilation, sourceTree);
    }
}
