using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class ValueObjectPropertyDiagnosticTests
{
    [Fact]
    public void ValueObject_TypedId_Property_Rewrites_To_Unwrap_Member()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int value) => Value = value;
            }

            [Validate]
            public readonly record struct PlaceOrderCommand(
                [property: GreaterThan(0)] CustomerId CustomerId);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var validatorSource = GetGeneratedSource(result, "PlaceOrderCommandValidator.g.cs");
        Assert.Contains("instance.CustomerId.Value", validatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Primitive_Property_Still_Emits_Raw_Access()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public readonly record struct PlaceOrderCommand(
                [property: GreaterThan(0)] int CustomerId);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var validatorSource = GetGeneratedSource(result, "PlaceOrderCommandValidator.g.cs");
        Assert.Contains("instance.CustomerId", validatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("instance.CustomerId.Value", validatorSource, StringComparison.Ordinal);
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult result, string filenameSuffix) =>
        result.GeneratedTrees
            .First(t => t.FilePath.EndsWith(filenameSuffix, StringComparison.Ordinal))
            .ToString();

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var valueObjectStub = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(valueObjectStub)],
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
