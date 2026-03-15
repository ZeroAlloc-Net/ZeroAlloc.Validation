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
