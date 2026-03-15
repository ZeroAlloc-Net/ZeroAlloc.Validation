using System;
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
        Assert.Contains("IsNullOrEmpty", generated, StringComparison.Ordinal);
        Assert.Contains("\"Name\"", generated, StringComparison.Ordinal);
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
        Assert.Contains(".Length > 50", generated, StringComparison.Ordinal);
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
        Assert.Contains("<= 0", generated, StringComparison.Ordinal);
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
        Assert.Contains("else if", generated, StringComparison.Ordinal);
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
        Assert.Contains("ValidationFailure[3]", generated, StringComparison.Ordinal);
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
            .First(s => s.Contains("CustomerValidator", StringComparison.Ordinal));

        Assert.Contains("List<", customerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("List<", RunGeneratorGetSources(source)
            .First(s => s.Contains("AddressValidator", StringComparison.Ordinal)), StringComparison.Ordinal);
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
            .First(s => s.Contains("CustomerValidator", StringComparison.Ordinal));

        Assert.Contains("AddressValidator", customerSource, StringComparison.Ordinal);
        Assert.Contains("\"Address.\" +", customerSource, StringComparison.Ordinal);
        Assert.Contains("is not null", customerSource, StringComparison.Ordinal);
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
            .First(s => s.Contains("OrderValidator", StringComparison.Ordinal));

        Assert.Contains("global::Models.Addresses.AddressValidator", orderSource, StringComparison.Ordinal);
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
            .First(s => s.Contains("CustomerValidator", StringComparison.Ordinal));

        Assert.Contains("is not null", customerSource, StringComparison.Ordinal);
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
            .First(s => s.Contains("OrderValidator", StringComparison.Ordinal));

        Assert.Contains("List<", orderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("List<", RunGeneratorGetSources(source)
            .First(s => s.Contains("LineItemValidator", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsCollectionValidation_WithBracketIndex()
    {
        var source = """
            using ZValidation;
            using System.Collections.Generic;
            namespace TestModels;

            [Validate]
            public class LineItem { [NotEmpty] public string Sku { get; set; } = ""; }

            [Validate]
            public class Order { public List<LineItem> Items { get; set; } = new(); }
            """;

        var orderSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("OrderValidator", StringComparison.Ordinal));

        Assert.Contains("LineItemValidator", orderSource, StringComparison.Ordinal);
        Assert.Contains("\"Items[\" +", orderSource, StringComparison.Ordinal);
        Assert.Contains("is not null", orderSource, StringComparison.Ordinal);
        Assert.Contains("foreach", orderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_DetectsArrayOfValidateType()
    {
        var source = """
            using ZValidation;
            namespace TestModels;

            [Validate]
            public class Tag { [NotEmpty] public string Name { get; set; } = ""; }

            [Validate]
            public class Article { public Tag[] Tags { get; set; } = []; }
            """;

        var articleSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("ArticleValidator", StringComparison.Ordinal));

        Assert.Contains("TagValidator", articleSource, StringComparison.Ordinal);
        Assert.Contains("List<", articleSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsNullGuard_ForCollectionProperty()
    {
        var source = """
            using ZValidation;
            using System.Collections.Generic;
            namespace TestModels;

            [Validate]
            public class Item { [NotEmpty] public string Name { get; set; } = ""; }

            [Validate]
            public class Bag { public List<Item>? Items { get; set; } }
            """;

        var bagSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("BagValidator", StringComparison.Ordinal));

        Assert.Contains("is not null", bagSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsNull_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [Null] public string? Name { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("is not null", generated, StringComparison.Ordinal);
        Assert.Contains("\"Name\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsEmpty_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [Empty] public string? Name { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("IsNullOrEmpty", generated, StringComparison.Ordinal);
        Assert.Contains("\"Name\"", generated, StringComparison.Ordinal);
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
