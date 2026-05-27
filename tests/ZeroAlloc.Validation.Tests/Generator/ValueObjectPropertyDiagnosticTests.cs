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

    [Fact]
    public void MultiProperty_ValueObject_With_BuiltIn_Validator_Fires_ZV0016()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct Money
            {
                public decimal Amount { get; }
                public string Currency { get; }
                public Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = currency;
                }
            }

            [Validate]
            public readonly record struct PriceCommand(
                [property: GreaterThan(0)] Money Total);
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0016", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleProperty_ValueObject_Does_Not_Fire_ZV0016()
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

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0016", StringComparison.Ordinal));
    }

    [Fact]
    public void Must_Predicate_On_ValueObject_Property_Passes_Wrapper_Not_Unwrap()
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
            public partial class PlaceOrderCommand
            {
                [Must(nameof(IsKnown))]
                public CustomerId CustomerId { get; set; }

                public bool IsKnown(CustomerId id) => id.Value > 0;
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var validatorSource = GetGeneratedSource(result, "PlaceOrderCommandValidator.g.cs");
        // The Must predicate receives the wrapper, NOT instance.CustomerId.Value.
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

        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(valueObjectStub)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
