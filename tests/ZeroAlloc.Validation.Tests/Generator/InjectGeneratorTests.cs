using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Generator;

public class InjectGeneratorTests
{
    [Fact]
    public void Generator_EmitsAddZeroAllocValidators_WithTwoModels()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            [Validate] public class Order    { [NotEmpty] public string Ref  { get; set; } = ""; }
            """;

        var generated = RunInjectGenerator(source);

        Assert.Contains("AddZeroAllocValidators",                          generated, System.StringComparison.Ordinal);
        Assert.Contains("TryAddSingleton",                                 generated, System.StringComparison.Ordinal);
        Assert.Contains("ValidatorFor<global::MyApp.Customer>",            generated, System.StringComparison.Ordinal);
        Assert.Contains("ValidatorFor<global::MyApp.Order>",               generated, System.StringComparison.Ordinal);
        Assert.Contains("global::MyApp.CustomerValidator",                 generated, System.StringComparison.Ordinal);
        Assert.Contains("global::MyApp.OrderValidator",                    generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NonValidateType_NotRegistered()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            public class NotAModel { public string X { get; set; } = ""; }
            """;

        var generated = RunInjectGenerator(source);

        Assert.DoesNotContain("NotAModel", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NoValidateClasses_EmitsNothing()
    {
        var source = """
            namespace MyApp;
            public class Plain { public string X { get; set; } = ""; }
            """;

        var trees = RunInjectGeneratorAllTrees(source);

        Assert.Empty(trees);
    }

    [Fact]
    public void Generator_EmitsForRecords()
    {
        // Regression for #174. RecordDeclarationSyntax is a sibling of
        // ClassDeclarationSyntax under TypeDeclarationSyntax, not a subtype, so a
        // predicate matching only classes skipped every [Validate] record and
        // AddZeroAllocValidators was never emitted. record is the idiomatic shape
        // for a validated DTO, and every test here previously used class, which is
        // why it shipped.
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public record Customer([property: NotEmpty] string Name);
            """;

        var generated = RunInjectGenerator(source);

        Assert.Contains("AddZeroAllocValidators", generated, System.StringComparison.Ordinal);
        Assert.Contains("Customer", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsForRecordStructs()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public readonly record struct Tag([property: NotEmpty] string Value);
            """;

        var generated = RunInjectGenerator(source);

        Assert.Contains("AddZeroAllocValidators", generated, System.StringComparison.Ordinal);
        Assert.Contains("Tag", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsForMixedClassesAndRecords()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Order { [NotEmpty] public string Ref { get; set; } = ""; }
            [Validate] public record Customer([property: NotEmpty] string Name);
            """;

        var generated = RunInjectGenerator(source);

        Assert.Contains("Order", generated, System.StringComparison.Ordinal);
        Assert.Contains("Customer", generated, System.StringComparison.Ordinal);
    }

    private static string RunInjectGenerator(string source)
        => RunInjectGeneratorAllTrees(source).First();

    private static System.Collections.Generic.IReadOnlyList<string> RunInjectGeneratorAllTrees(string source)
    {
        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ZeroAlloc.Validation.Inject.InjectGenerator();
        var driver    = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
    }
}
