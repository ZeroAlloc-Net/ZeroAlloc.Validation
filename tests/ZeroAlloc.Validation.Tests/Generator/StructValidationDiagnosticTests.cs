using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class StructValidationDiagnosticTests
{
    [Fact]
    public void NonReadonly_RecordStruct_With_Validate_Fires_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public record struct MutableRs([property: GreaterThan(0)] int Total);
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0014", StringComparison.Ordinal));
    }

    [Fact]
    public void NonReadonly_Struct_With_Validate_Fires_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public struct MutableS
            {
                [GreaterThan(0)]
                public int Total { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0014", StringComparison.Ordinal));
    }

    [Fact]
    public void ZV0014_Names_A_Nested_Struct_By_Its_Qualified_Name()
    {
        // Two nested models may share a simple name, so the message must tell them apart.
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public static class First
            {
                [Validate]
                public struct Request
                {
                    [GreaterThan(0)]
                    public int Total { get; set; }
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Equal(1, result.Diagnostics.Count(d => string.Equals(d.Id, "ZV0014", StringComparison.Ordinal)));
        var zv0014 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0014", StringComparison.Ordinal));
        Assert.StartsWith(
            "Struct 'TestModels.First.Request' is decorated with [Validate]",
            zv0014.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Readonly_RecordStruct_With_Validate_Does_Not_Fire_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public readonly record struct CleanRrs([property: GreaterThan(0)] int Total);
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0014", StringComparison.Ordinal));
    }

    [Fact]
    public void Readonly_Struct_With_Validate_Does_Not_Fire_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public readonly struct CleanRs
            {
                [GreaterThan(0)]
                public int Total { get; }
                public CleanRs(int total) => Total = total;
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0014", StringComparison.Ordinal));
    }

    [Fact]
    public void Class_With_Validate_Does_Not_Fire_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public class CleanClass
            {
                [GreaterThan(0)]
                public int Total { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0014", StringComparison.Ordinal));
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
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
