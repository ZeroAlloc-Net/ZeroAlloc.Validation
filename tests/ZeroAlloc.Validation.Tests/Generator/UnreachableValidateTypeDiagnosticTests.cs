using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;
using ZeroAlloc.Validation.Options.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Guards issue #216. The generated validator is a top-level class in its own file, so it cannot
/// name a <c>[Validate]</c> type that is <c>private</c>, <c>protected</c> or
/// <c>private protected</c>, nested inside such a type, or <c>file</c>-local. A private nested
/// type used to be skipped without a word, which dropped the attribute and every nested
/// diagnostic with it; the others produced generated code that did not compile. ZV0025 now
/// reports the attribute, and no generator emits code that names the type. A nested type the
/// validator can reach is supported, issue #207, and is not reported.
/// </summary>
public class UnreachableValidateTypeDiagnosticTests
{
    private const string Rule = """[NotEmpty] public string Name { get; set; } = "";""";

    /// <summary>Each source declares one unreachable [Validate] type, and the name ZV0025 gives it.</summary>
    public static TheoryData<string, string> UnreachableTypes() => new()
    {
        { $$"""public class Outer { [Validate] private class Inner { {{Rule}} } }""", "MyApp.Outer.Inner" },
        { $$"""public class Outer { [Validate] protected class Inner { {{Rule}} } }""", "MyApp.Outer.Inner" },
        { $$"""public class Outer { [Validate] private protected class Inner { {{Rule}} } }""", "MyApp.Outer.Inner" },
        { $$"""public class Outer { private class Hidden { [Validate] public class Inner { {{Rule}} } } }""", "MyApp.Outer.Hidden.Inner" },
        { $$"""public class Outer { protected class Holder { [Validate] internal class Inner { {{Rule}} } } }""", "MyApp.Outer.Holder.Inner" },
        { $$"""public class Outer { private protected class Holder { [Validate] public class Inner { {{Rule}} } } }""", "MyApp.Outer.Holder.Inner" },
        { $$"""[Validate] file class Inner { {{Rule}} }""", "MyApp.Inner" },
        { $$"""file class Outer { [Validate] public class Inner { {{Rule}} } }""", "MyApp.Outer.Inner" },
    };

    public static TheoryData<string> ReachableNestedAccessibilities() => new()
    {
        "public",
        "internal",
        "protected internal",
    };

    [Theory]
    [MemberData(nameof(UnreachableTypes))]
    public void UnreachableValidateType_ReportsZV0025AtTheAttribute(string declaration, string displayName)
    {
        var (compilation, diagnostics, generated) = Run(Source(declaration), new ValidatorGenerator());

        Assert.Equal(1, diagnostics.Count(d => string.Equals(d.Id, "ZV0025", StringComparison.Ordinal)));
        var zv0025 = diagnostics.First(d => string.Equals(d.Id, "ZV0025", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, zv0025.Severity);
        Assert.Equal("Validate", SourceAt(zv0025));
        Assert.Equal(
            $"The generated validator cannot access '{displayName}', so no validator is generated for it; "
                + "make it and every type containing it internal or public",
            zv0025.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(generated, s => s.Contains("InnerValidator", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
    }

    [Theory]
    [MemberData(nameof(UnreachableTypes))]
    public void UnreachableValidateType_IsLeftOutOfTheInjectAndOptionsGlue(string declaration, string displayName)
    {
        _ = displayName;
        var source = Source(declaration) + """

            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }

            internal static class Wiring
            {
                public static void Wire(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    ZeroAlloc.Validation.Options.ZeroAllocOptionsValidationExtensions.ValidateWithZeroAlloc(
                        Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.AddOptions<Customer>(services));
                    services.AddZeroAllocValidators();
                }
            }
            """;

        var (compilation, _, generated) = Run(
            source,
            new ValidatorGenerator(),
            new OptionsValidationEmitter(),
            new global::ZeroAlloc.Validation.Inject.InjectGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Contains(generated, s => s.Contains("global::MyApp.CustomerValidator>", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, s => s.Contains("Inner", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(UnreachableTypes))]
    public void UnreachableValidateType_IsLeftOutOfTheAspNetCoreGlue(string declaration, string displayName)
    {
        // This host does not reference ASP.NET Core, so only the emitted text is checked; the
        // glue is compiled and run for real in ZeroAlloc.Validation.Tests.AspNetCore.
        _ = displayName;
        var source = Source(declaration) + """

            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var (_, _, generated) = Run(source, new global::ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter());

        Assert.Contains(generated, s => s.Contains("global::MyApp.CustomerValidator>", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, s => s.Contains("Inner", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ReachableNestedAccessibilities))]
    public void ReachableNestedValidateType_IsNotReported_AndItsValidatorCompiles(string accessibility)
    {
        var source = Source($$"""public class Outer { [Validate] {{accessibility}} class Inner { {{Rule}} } }""");

        var (compilation, diagnostics, _) = Run(
            source,
            new ValidatorGenerator(),
            new OptionsValidationEmitter(),
            new global::ZeroAlloc.Validation.Inject.InjectGenerator());

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZV0025", StringComparison.Ordinal));
        Assert.Empty(Errors(compilation));
        Assert.NotNull(compilation.GetTypeByMetadataName("MyApp.Outer_InnerValidator"));

        // This host does not reference ASP.NET Core, so the filter is checked as emitted text.
        var (_, _, filterSources) = Run(source, new global::ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter());
        Assert.Contains(filterSources, s => s.Contains("case global::MyApp.Outer.Inner ", StringComparison.Ordinal));
    }

    [Fact]
    public void PropertyOfUnreachableValidateType_IsNotWiredToAValidatorThatIsNeverGenerated()
    {
        // Outer is reachable and gets a validator. Its properties name Inner, which gets none,
        // so Outer's validator must not take one as a constructor dependency.
        var source = Source($$"""
            [Validate]
            public class Outer
            {
                [Validate] private class Inner { {{Rule}} }

                [NotEmpty] public string Id { get; set; } = "";
                private Inner? First { get; set; }
                private System.Collections.Generic.List<Inner> Lines { get; set; } = new();
            }
            """);

        var (compilation, diagnostics, generated) = Run(source, new ValidatorGenerator());

        Assert.Equal(["ZV0025"], diagnostics.ConvertAll(d => d.Id));
        Assert.Empty(Errors(compilation));
        Assert.DoesNotContain(generated, s => s.Contains("InnerValidator", StringComparison.Ordinal));
        // Inner is no dependency of Outer's validator, so it does not make that validator internal.
        Assert.Equal(Accessibility.Public, compilation.GetTypeByMetadataName("MyApp.OuterValidator")?.DeclaredAccessibility);
    }

    [Fact]
    public void PrivateNestedValidateType_ReportsOnlyZV0025()
    {
        // Before #216 this type was skipped silently, so the misplaced field rule below was
        // dropped with it and ZV0024 never ran. The rules on a type the generator cannot
        // validate are moot: ZV0025 is the one actionable error, and once the type is reachable
        // every other diagnostic runs as usual.
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace MyApp;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            public class Outer
            {
                [Validate]
                private struct Inner
                {
                    [NotBlank] public string? Code;
                }
            }
            """;

        var (compilation, diagnostics, _) = Run(source, new ValidatorGenerator());

        Assert.Empty(Errors(compilation));
        Assert.Equal(["ZV0025"], diagnostics.ConvertAll(d => d.Id));
    }

    /// <summary>
    /// An unreachable <c>[Validate]</c> base type under a reachable model. C# rejects both, since
    /// a type cannot be more accessible than its base, so the source does not compile; the
    /// generator still runs on it and must not lose the base type's usages.
    /// </summary>
    /// <remarks>
    /// A member of a <c>private</c> nested type is itself out of the validator's reach, so the
    /// model reports it as ZV0017 rather than ZV0013; a <c>file</c>-local type's member is
    /// accessible within the assembly, so its bad signature is ZV0013.
    /// </remarks>
    public static TheoryData<string, string, string> UnreachableValidateBases() => new()
    {
        {
            """
            public class Outer
            {
                [Validate] private class RequestBase { [CustomValidation] public int Check() => 0; }

                [Validate] public class Request : RequestBase { [NotEmpty] public string Name { get; set; } = ""; }
            }
            """,
            "ZV0017",
            "Check"
        },
        {
            """
            [Validate] file class RequestBase { [CustomValidation] public int Check() => 0; }

            [Validate] public class Request : RequestBase { [NotEmpty] public string Name { get; set; } = ""; }
            """,
            "ZV0013",
            "CustomValidation"
        },
    };

    [Theory]
    [MemberData(nameof(UnreachableValidateBases))]
    public void UsageOnAnUnreachableValidateBase_IsReportedByTheReachableModel(string declaration, string expectedId, string expectedSpan)
    {
        // The base type returns at ZV0025 and reports none of its members, so the model must not
        // defer its base's usages to it: the [CustomValidation] method is reported once, by the
        // model, issue #236.
        var (_, diagnostics, _) = Run(Source(declaration), new ValidatorGenerator());

        Assert.Equal(1, diagnostics.Count(d => string.Equals(d.Id, "ZV0025", StringComparison.Ordinal)));
        var others = diagnostics.FindAll(d => !string.Equals(d.Id, "ZV0025", StringComparison.Ordinal));
        Assert.Equal([expectedId], others.ConvertAll(d => d.Id));
        Assert.Equal(expectedSpan, SourceAt(others[0]));
    }

    private static string Source(string declaration) => $$"""
        using ZeroAlloc.Validation;
        namespace MyApp;

        {{declaration}}
        """;

    private static string SourceAt(Diagnostic diagnostic)
    {
        var tree = diagnostic.Location.SourceTree;
        Assert.NotNull(tree);
        return tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }

    private static List<string> Errors(Compilation compilation)
    {
        var errors = new List<string>();
        foreach (var d in compilation.GetDiagnostics())
        {
            // Id and message only: the default formatting leads with the generated file path.
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add($"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        return errors;
    }

    /// <summary>
    /// Runs the generators over <paramref name="source"/>, referencing this test host's own
    /// dependencies, and returns the updated compilation, the generator diagnostics and the text
    /// of every generated file.
    /// </summary>
    private static (Compilation Compilation, List<Diagnostic> Diagnostics, List<string> Generated) Run(
        string source, params IIncrementalGenerator[] generators)
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

        var driver = CSharpGeneratorDriver.Create(generators)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generated = new List<string>();
        foreach (var result in driver.GetRunResult().Results)
        {
            foreach (var file in result.GeneratedSources)
                generated.Add(file.SourceText.ToString());
        }

        return (output, new List<Diagnostic>(diagnostics), generated);
    }
}
