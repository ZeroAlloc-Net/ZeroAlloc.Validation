using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class NestedValidatorValueTypeTests
{
    [Fact]
    public void Collection_Of_ReadonlyRecordStruct_Compiles_WithoutItemNullGuard()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed record Outer([property: NotEmpty] IReadOnlyList<Item> Items);

            [Validate]
            public readonly record struct Item([property: GreaterThan(0)] int Qty);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var outerValidator = GetGeneratedSource(result, "OuterValidator.g.cs");
        Assert.DoesNotContain("_c0Item is not null", outerValidator, StringComparison.Ordinal);
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult result, string filenameSuffix) =>
        result.GeneratedTrees
            .First(t => t.FilePath.EndsWith(filenameSuffix, StringComparison.Ordinal))
            .ToString();

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
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
