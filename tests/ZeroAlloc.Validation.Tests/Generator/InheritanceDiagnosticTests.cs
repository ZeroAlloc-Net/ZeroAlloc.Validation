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

    private static string GeneratedSource(string source)
    {
        var result = RunGenerator(source);
        return string.Join(
            "\n",
            result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        // The full trusted-platform set, so the generated validator can be compiled for real
        // rather than only inspected as text.
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

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var driver = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGenerators(CreateCompilation(source));
        return driver.GetRunResult();
    }

    private static Diagnostic[] CompileWithGenerator(string source)
    {
        CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(CreateCompilation(source), out var output, out _);

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
