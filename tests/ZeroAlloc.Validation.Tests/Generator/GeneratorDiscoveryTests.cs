using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZValidation.Generator;

namespace ZValidation.Tests.Generator;

public class GeneratorDiscoveryTests
{
    [Fact]
    public void Generator_ProducesOutput_ForValidateClass()
    {
        var source = """
            using ZValidation;

            namespace TestModels;

            [Validate]
            public class Person
            {
                [NotEmpty]
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_ProducesNoOutput_ForClassWithoutValidateAttribute()
    {
        var source = """
            namespace TestModels;
            public class Person { public string Name { get; set; } = ""; }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_EmitsValidatorClass_InSameNamespace()
    {
        var source = """
            using ZValidation;
            namespace TestModels;

            [Validate]
            public class Person
            {
                [NotEmpty]
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        var generated = result.GeneratedTrees[0].ToString();

        Assert.Contains("namespace TestModels", generated, System.StringComparison.Ordinal);
        Assert.Contains("class PersonValidator", generated, System.StringComparison.Ordinal);
        Assert.Contains("ValidatorFor<Person>", generated, System.StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(compilation);

        return driver.GetRunResult();
    }
}
