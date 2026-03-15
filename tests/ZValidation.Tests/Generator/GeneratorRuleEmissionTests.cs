using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZValidation;
using ZValidation.Generator;

namespace ZValidation.Tests.Generator;

public class GeneratorRuleEmissionTests
{
    [Fact]
    public void Generator_EmitsNotEmpty_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("IsNullOrEmpty", generated);
        Assert.Contains("\"Name\"", generated);
    }

    [Fact]
    public void Generator_EmitsMaxLength_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person { [MaxLength(50)] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains(".Length > 50", generated);
    }

    [Fact]
    public void Generator_EmitsGreaterThan_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person { [GreaterThan(0)] public int Age { get; set; } }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("<= 0", generated);
    }

    [Fact]
    public void Generator_EmitsStopAtFirstFailure_AsElseIf()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person
            {
                [NotEmpty]
                [MaxLength(50)]
                public string Name { get; set; } = "";
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("else if", generated);
    }

    [Fact]
    public void Generator_EmitsStackalloc_SizedToRuleCount()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person
            {
                [NotEmpty]
                [MaxLength(50)]
                public string Name { get; set; } = "";
                [GreaterThan(0)]
                public int Age { get; set; }
            }
            """;

        // 3 rules total
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("ValidationFailure[3]", generated);
    }

    [Fact]
    public void Generator_UsesListForModelWithNestedValidateType()
    {
        var source = """
            using ZValidation;
            namespace TestModels;

            [Validate]
            public class Address
            {
                [NotEmpty]
                public string Street { get; set; } = "";
            }

            [Validate]
            public class Customer
            {
                [NotEmpty]
                public string Name { get; set; } = "";
                public Address Address { get; set; } = new();
            }
            """;

        var customerSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("CustomerValidator"));

        Assert.Contains("List<", customerSource);
        Assert.DoesNotContain("List<", RunGeneratorGetSources(source)
            .First(s => s.Contains("AddressValidator")));
    }

    [Fact]
    public void Generator_EmitsNestedValidation_WithDotPrefix()
    {
        var source = """
            using ZValidation;
            namespace TestModels;

            [Validate]
            public class Address
            {
                [NotEmpty]
                public string Street { get; set; } = "";
            }

            [Validate]
            public class Customer
            {
                public Address Address { get; set; } = new();
            }
            """;

        var customerSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("CustomerValidator"));

        Assert.Contains("AddressValidator", customerSource);
        Assert.Contains("\"Address.\" +", customerSource);
        Assert.Contains("is not null", customerSource);
    }

    [Fact]
    public void Generator_EmitsFullyQualifiedValidatorName_ForCrossNamespaceNestedType()
    {
        var source = """
            namespace Models.Addresses
            {
                [ZValidation.Validate]
                public class Address { [ZValidation.NotEmpty] public string Street { get; set; } = ""; }
            }
            namespace Models.Orders
            {
                [ZValidation.Validate]
                public class Order { public Models.Addresses.Address Shipping { get; set; } = new(); }
            }
            """;

        var orderSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("OrderValidator"));

        Assert.Contains("global::Models.Addresses.AddressValidator", orderSource);
    }

    [Fact]
    public void Generator_EmitsNullGuard_ForNullableNestedProperty()
    {
        // The generated code must have the null guard
        var source = """
            using ZValidation;
            namespace TestModels;

            [Validate]
            public class Address { [NotEmpty] public string Street { get; set; } = ""; }

            [Validate]
            public class Customer { public Address? Address { get; set; } }
            """;

        var customerSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("CustomerValidator"));

        Assert.Contains("is not null", customerSource);
    }

    [Fact]
    public void Generator_UsesListForModelWithCollectionOfValidateType()
    {
        var source = """
            using ZValidation;
            using System.Collections.Generic;
            namespace TestModels;

            [Validate]
            public class LineItem
            {
                [NotEmpty]
                public string Sku { get; set; } = "";
            }

            [Validate]
            public class Order
            {
                [NotEmpty]
                public string Reference { get; set; } = "";
                public List<LineItem> LineItems { get; set; } = new();
            }
            """;

        var orderSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("OrderValidator"));

        Assert.Contains("List<", orderSource);
        Assert.DoesNotContain("List<", RunGeneratorGetSources(source)
            .First(s => s.Contains("LineItemValidator")));
    }

    private static string RunGeneratorGetSource(string source)
    {
        // Include System.Runtime so Roslyn can fully resolve attribute constructor argument types.
        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var result = driver.GetRunResult();
        return result.GeneratedTrees[0].ToString();
    }

    private static IReadOnlyList<string> RunGeneratorGetSources(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(
                    System.IO.Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
    }
}
