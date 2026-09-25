using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// The generated validator holds one field and takes one constructor parameter per nested or
/// collection property, named after the property in camel case. Two properties whose names
/// differ only in the case of the first letter, <c>Address</c> and <c>address</c>, gave both the
/// same name, so the validator did not compile. The later one now takes the first free numeric
/// suffix, and every other model keeps the names it always had. The runtime counterpart is
/// <c>Integration/CaseOnlyNamedModelTests</c>.
/// </summary>
public class NestedValidatorFieldNameTests
{
    [Fact]
    public void CaseOnlyDifferentProperties_LaterOneTakesTheFirstFreeSuffix()
    {
        // Address2 claims address2 for itself, so address, the second to want address, gets
        // address3.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;

            [Validate] public class Child { [NotEmpty] public string? Name { get; set; } }

            [Validate]
            public class Model
            {
                public Child? Address { get; set; }
                public Child? address { get; set; }
                public System.Collections.Generic.List<Child> Address2 { get; set; } = new();
            }
            """;

        var (compilation, validator) = Run(source);

        Assert.Empty(Errors(compilation));
        Assert.Contains(
            "public ModelValidator(global::MyApp.ChildValidator addressValidator, "
                + "global::MyApp.ChildValidator address3Validator, global::MyApp.ChildValidator address2Validator)",
            validator, StringComparison.Ordinal);
        Assert.Contains("_addressValidator.Validate(instance.Address)", validator, StringComparison.Ordinal);
        Assert.Contains("_address3Validator.Validate(instance.address)", validator, StringComparison.Ordinal);
        Assert.Contains("_address2Validator.Validate(_c0Item)", validator, StringComparison.Ordinal);
        Assert.Contains(
            "/// <param name=\"address3Validator\">The validator for the nested <c>address</c> member.</param>",
            validator, StringComparison.Ordinal);
        Assert.Contains(
            "/// <param name=\"addressValidator\">The validator for the nested <c>Address</c> member.</param>",
            validator, StringComparison.Ordinal);
    }

    [Fact]
    public void CaseOnlyDifferentProperties_UnderModelLevelFailFast_Compile()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;

            [Validate] public class Child { [NotEmpty] public string? Name { get; set; } }

            [Validate(StopOnFirstFailure = true)]
            public class Model
            {
                public Child? Address { get; set; }
                public System.Collections.Generic.List<Child> address { get; set; } = new();
            }
            """;

        var (compilation, validator) = Run(source);

        Assert.Empty(Errors(compilation));
        Assert.Contains("_address2Validator.Validate(_c0Item)", validator, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelWithoutCaseOnlyPair_KeepsItsNames()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;

            [Validate] public class Child { [NotEmpty] public string? Name { get; set; } }

            [Validate]
            public class Model
            {
                public Child? Address { get; set; }
                public Child? billing { get; set; }
                public System.Collections.Generic.List<Child> Lines { get; set; } = new();
            }
            """;

        var (compilation, validator) = Run(source);

        Assert.Empty(Errors(compilation));
        Assert.Contains(
            "public ModelValidator(global::MyApp.ChildValidator addressValidator, "
                + "global::MyApp.ChildValidator billingValidator, global::MyApp.ChildValidator linesValidator)",
            validator, StringComparison.Ordinal);
        Assert.Contains(
            "/// <param name=\"billingValidator\">The validator for the nested <c>Billing</c> member.</param>",
            validator, StringComparison.Ordinal);
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
    /// Runs the validator generator over <paramref name="source"/> and returns the updated
    /// compilation and the text of the generated <c>ModelValidator</c>.
    /// </summary>
    private static (Compilation Compilation, string Validator) Run(string source)
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

        var driver = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var validator = "";
        foreach (var file in driver.GetRunResult().Results[0].GeneratedSources)
        {
            if (file.HintName.EndsWith("MyApp.ModelValidator.g.cs", StringComparison.Ordinal))
                validator = file.SourceText.ToString();
        }

        return (output, validator);
    }
}
