using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class InheritanceDiagnosticTests
{
    [Fact]
    public void ProtectedBaseMemberWithRules_Fires_ZV0017()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class AuditBase
            {
                [NotEmpty]
                protected string? ModifiedBy { get; init; }
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
    }

    [Fact]
    public void ProtectedBaseMemberWithRules_DoesNotEmitInaccessibleReference()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class AuditBase
            {
                [NotEmpty]
                protected string? ModifiedBy { get; init; }
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        // The rule is dropped rather than emitted as code that would not compile.
        Assert.DoesNotContain("ModifiedBy", GeneratedSource(source), StringComparison.Ordinal);
    }

    [Fact]
    public void PublicBaseMemberWithRules_DoesNotFire_ZV0017()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class AuditBase
            {
                [NotEmpty]
                public string? ModifiedBy { get; init; }
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
    }

    [Fact]
    public void IncludeBasePropertiesFalse_DoesNotFire_ZV0017()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class AuditBase
            {
                [NotEmpty]
                protected string? ModifiedBy { get; init; }
            }

            [Validate(IncludeBaseProperties = false)]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
    }

    [Fact]
    public void InternalBaseMemberInSameAssembly_IsValidated()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class AuditBase
            {
                [NotEmpty]
                internal string? ModifiedBy { get; init; }
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        Assert.Contains("ModifiedBy", GeneratedSource(source), StringComparison.Ordinal);
    }

    [Fact]
    public void InheritedRules_GenerateCompilableValidator()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            public abstract class AuditBase
            {
                [NotEmpty]
                public string? ModifiedBy { get; init; }

                [NotEmpty]
                protected string? Secret { get; init; }

                [CustomValidation]
                public IEnumerable<ValidationFailure> ValidateAudit()
                {
                    yield break;
                }
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        Assert.Empty(CompileWithGenerator(source));
    }

    [Fact]
    public void InheritedRuleWithProtectedWhenMethod_GeneratesCompilableValidator()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public abstract class AuditBase
            {
                [NotEmpty(When = nameof(ShouldCheck))]
                public string? ModifiedBy { get; init; }

                protected bool ShouldCheck() => true;
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        Assert.Empty(CompileWithGenerator(source));
    }

    [Fact]
    public void InheritedRuleWithProtectedWhenMethod_Fires_ZV0017()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public abstract class AuditBase
            {
                [NotEmpty(When = nameof(ShouldCheck))]
                public string? ModifiedBy { get; init; }

                protected bool ShouldCheck() => true;
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        var result = RunGenerator(source);

        // The rule is dropped rather than emitted as an uncompilable call — say so out loud.
        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
    }

    // Roslyn imports a referenced assembly's internal members only when that assembly grants
    // the compilation its internals, so the members the test without the grant expects reported
    // are protected internal.
    private const string InternalsBaseLibrary = """
        using System.Collections.Generic;
        using ZeroAlloc.Validation;
        namespace BaseLib;

        public class AuditBase
        {
            [NotEmpty]
            internal string? ModifiedBy { get; init; }

            [NotEmpty]
            public string? Reviewer { protected internal get; init; }

            [CustomValidation]
            protected internal IEnumerable<ValidationFailure> CheckAudit()
            {
                yield break;
            }
        }
        """;

    private const string InternalsDerivedModel = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Audited : BaseLib.AuditBase
        {
            [GreaterThan(0)]
            public int Total { get; init; }
        }
        """;

    [Fact]
    public void InternalBaseMembersInAssemblyGrantingInternalsVisibleTo_AreValidated()
    {
        // The generated validator compiles into TestAssembly, which BaseLib grants its internals,
        // so the internal members and the internal getter are reachable, issue #227.
        var compilation = CreateCompilation(
            InternalsDerivedModel,
            CompileLibrary(
                InternalsBaseLibrary,
                "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]"));

        var result = RunGenerator(compilation);
        var generated = GeneratedSource(result);

        Assert.Empty(ZeroAllocDiagnosticIds(result));
        Assert.Contains("ModifiedBy", generated, StringComparison.Ordinal);
        Assert.Contains("Reviewer", generated, StringComparison.Ordinal);
        Assert.Contains("CheckAudit", generated, StringComparison.Ordinal);
        Assert.Empty(CompileWithGenerator(compilation));
    }

    [Fact]
    public void InternalBaseMembersInAnotherAssembly_WithoutInternalsVisibleTo_Fire_ZV0017()
    {
        var compilation = CreateCompilation(InternalsDerivedModel, CompileLibrary(InternalsBaseLibrary));

        var result = RunGenerator(compilation);

        var reported = result.Diagnostics
            .Where(d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal))
            .Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(2, reported.Length);
        Assert.Contains(reported, m => m.Contains("Reviewer", StringComparison.Ordinal));
        Assert.Contains(reported, m => m.Contains("CheckAudit", StringComparison.Ordinal));
        var generated = GeneratedSource(result);
        Assert.DoesNotContain("ModifiedBy", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Reviewer", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckAudit", generated, StringComparison.Ordinal);
        Assert.Empty(CompileWithGenerator(compilation));
    }

    [Fact]
    public void ProtectedOverrideOfProtectedCustomValidationMethod_Fires_ZV0017Once()
    {
        // The override inherits the attribute, issue #240. The base declaration already reports
        // the inaccessible method, so the override adds no ZV0028 of its own.
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class AuditBase
            {
                [CustomValidation]
                protected virtual IEnumerable<ValidationFailure> CheckAudit()
                {
                    yield break;
                }
            }

            [Validate]
            public class Audited : AuditBase
            {
                protected override IEnumerable<ValidationFailure> CheckAudit()
                {
                    yield break;
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Equal(["ZV0017"], ZeroAllocDiagnosticIds(result));
        Assert.Empty(CompileWithGenerator(source));
    }

    [Fact]
    public void OverrideOfInvalidCustomValidationMethodOnValidateBase_Fires_ZV0013Once()
    {
        // The [Validate] base type reports the signature. Its subtype's override inherits the
        // attribute and the signature, and does not report it again.
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public class AuditBase
            {
                [CustomValidation]
                public virtual bool CheckAudit() => true;
            }

            [Validate]
            public class Audited : AuditBase
            {
                public override bool CheckAudit() => false;
            }
            """;

        Assert.Equal(["ZV0013"], ZeroAllocDiagnosticIds(RunGenerator(source)));
    }

    [Fact]
    public void OverrideOfInvalidCustomValidationMethod_Fires_ZV0013()
    {
        // A base type that is not [Validate] reports nothing, so the model reports the signature
        // its override inherits rather than dropping the check without a word.
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class AuditBase
            {
                [CustomValidation]
                public virtual bool CheckAudit() => true;
            }

            [Validate]
            public class Audited : AuditBase
            {
                public override bool CheckAudit() => false;
            }
            """;

        Assert.Equal(["ZV0013"], ZeroAllocDiagnosticIds(RunGenerator(source)));
    }

    [Fact]
    public void OverrideChainWithoutCustomValidationAttribute_EmitsOneCall()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            public abstract class AuditRoot
            {
                [CustomValidation]
                public abstract IEnumerable<ValidationFailure> CheckAudit();
            }

            public class AuditBase : AuditRoot
            {
                public override IEnumerable<ValidationFailure> CheckAudit()
                {
                    yield break;
                }
            }

            [Validate]
            public class Audited : AuditBase
            {
                public sealed override IEnumerable<ValidationFailure> CheckAudit()
                {
                    yield break;
                }
            }
            """;

        Assert.Equal(1, CountOccurrences(GeneratedSource(source), ".CheckAudit()"));
        Assert.Empty(CompileWithGenerator(source));
    }

    [Fact]
    public void NewHiddenMethod_DoesNotInheritCustomValidation()
    {
        // `new` starts a new method with no OverriddenMethod, so it carries no attribute, and it
        // hides the base method from instance.CheckAudit(): nothing is called or reported.
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class AuditBase
            {
                [CustomValidation]
                public virtual IEnumerable<ValidationFailure> CheckAudit()
                {
                    yield break;
                }
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }

                public new IEnumerable<ValidationFailure> CheckAudit()
                {
                    yield break;
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(ZeroAllocDiagnosticIds(result));
        Assert.DoesNotContain("CheckAudit", GeneratedSource(result), StringComparison.Ordinal);
        Assert.Empty(CompileWithGenerator(source));
    }

    [Fact]
    public void ChainWithAttributeRepeatedInTheMiddle_EmitsOneCall()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            public abstract class AuditRoot
            {
                [CustomValidation]
                public abstract IEnumerable<ValidationFailure> CheckAudit();
            }

            public class AuditBase : AuditRoot
            {
                [CustomValidation]
                public override IEnumerable<ValidationFailure> CheckAudit()
                {
                    yield break;
                }
            }

            [Validate]
            public class Audited : AuditBase
            {
                public override IEnumerable<ValidationFailure> CheckAudit()
                {
                    yield break;
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(ZeroAllocDiagnosticIds(result));
        Assert.Equal(1, CountOccurrences(GeneratedSource(result), ".CheckAudit()"));
        Assert.Empty(CompileWithGenerator(source));
    }

    [Fact]
    public void ChainWithAttributeRepeatedInTheMiddle_ReportsAtTheNearestAttributedAncestor()
    {
        // The leaf inherits the attribute from the middle override, the nearest one that carries
        // it, so ZV0013 is reported once, at the middle's attribute.
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public abstract class AuditRoot
            {
                [CustomValidation]
                public abstract bool CheckAudit();
            }

            public class AuditBase : AuditRoot
            {
                [CustomValidation] // middle
                public override bool CheckAudit() => true;
            }

            [Validate]
            public class Audited : AuditBase
            {
                public override bool CheckAudit() => false;
            }
            """;

        var result = RunGenerator(source);

        Assert.Equal(["ZV0013"], ZeroAllocDiagnosticIds(result));
        var diagnostic = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0013", StringComparison.Ordinal));
        var line = diagnostic.Location.SourceTree!.GetText().Lines[diagnostic.Location.GetLineSpan().StartLinePosition.Line];
        Assert.Contains("// middle", line.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateProtectedBaseMemberInAssemblyGrantingInternalsVisibleTo_Fires_ZV0017()
    {
        // private protected needs both the grant and derivation; the validator only has the grant.
        const string library = """
            using ZeroAlloc.Validation;
            namespace BaseLib;

            public class AuditBase
            {
                [NotEmpty]
                private protected string? ModifiedBy { get; init; }
            }
            """;
        var compilation = CreateCompilation(
            InternalsDerivedModel,
            CompileLibrary(
                library,
                "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]"));

        var result = RunGenerator(compilation);

        Assert.Equal(["ZV0017"], ZeroAllocDiagnosticIds(result));
        Assert.DoesNotContain("ModifiedBy", GeneratedSource(result), StringComparison.Ordinal);
        Assert.Empty(CompileWithGenerator(compilation));
    }

    private static string[] ZeroAllocDiagnosticIds(GeneratorDriverRunResult result) =>
        result.Diagnostics
            .Select(d => d.Id)
            .Where(id => id.StartsWith("ZV", StringComparison.Ordinal))
            .ToArray();

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string GeneratedSource(string source) => GeneratedSource(RunGenerator(source));

    private static string GeneratedSource(GeneratorDriverRunResult result) =>
        string.Join(
            "\n",
            result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

    // The full trusted-platform set, so the generated validator can be compiled for real
    // rather than only inspected as text.
    private static IEnumerable<MetadataReference> TrustedReferences() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(System.IO.Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

    private static CSharpCompilation CreateCompilation(string source, params MetadataReference[] references) =>
        CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            TrustedReferences().Concat(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>
    /// Compiles <paramref name="sources"/> as the assembly <c>BaseLib</c> and references it as
    /// metadata, as a base type from a package would be.
    /// </summary>
    private static MetadataReference CompileLibrary(params string[] sources)
    {
        var library = CSharpCompilation.Create(
            "BaseLib",
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            TrustedReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new System.IO.MemoryStream();
        var emitted = library.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static GeneratorDriverRunResult RunGenerator(string source) => RunGenerator(CreateCompilation(source));

    private static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        var driver = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGenerators(compilation);
        return driver.GetRunResult();
    }

    private static Diagnostic[] CompileWithGenerator(string source) => CompileWithGenerator(CreateCompilation(source));

    private static Diagnostic[] CompileWithGenerator(CSharpCompilation compilation)
    {
        CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var diagnostics = output.GetDiagnostics();
        var errors = new List<Diagnostic>();
        foreach (var d in diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add(d);
        }
        return errors.ToArray();
    }
}
