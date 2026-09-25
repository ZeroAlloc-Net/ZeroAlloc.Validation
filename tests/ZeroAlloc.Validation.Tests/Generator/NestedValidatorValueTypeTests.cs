using System;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

public class NestedValidatorValueTypeTests
{
    private static GeneratorDriverRunResult RunGenerator(string source) =>
        GeneratorTestHelper.RunGenerator(source);

    private static string GetGeneratedSource(GeneratorDriverRunResult result, string hintNameSuffix) =>
        GeneratorTestHelper.GetGeneratedSource(result, $"TestModels.{hintNameSuffix}");

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

    [Fact]
    public void Collection_Of_Class_Still_Emits_ItemNullGuard()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed record Outer([property: NotEmpty] IReadOnlyList<Item> Items);

            [Validate]
            public sealed record Item([property: GreaterThan(0)] int Qty);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var outerValidator = GetGeneratedSource(result, "OuterValidator.g.cs");
        Assert.Contains("_c0Item is not null", outerValidator, StringComparison.Ordinal);
    }

    [Fact]
    public void Scalar_NestedValidator_Of_ReadonlyRecordStruct_Compiles_WithoutNullGuard()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed record Outer(Inner Inner);

            [Validate]
            public readonly record struct Inner([property: GreaterThan(0)] int Qty);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var outerValidator = GetGeneratedSource(result, "OuterValidator.g.cs");
        Assert.DoesNotContain("instance.Inner is not null", outerValidator, StringComparison.Ordinal);
    }

    [Fact]
    public void Scalar_NestedValidator_Of_Class_Still_Emits_NullGuard()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed record Outer(Inner Inner);

            [Validate]
            public sealed record Inner([property: GreaterThan(0)] int Qty);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var outerValidator = GetGeneratedSource(result, "OuterValidator.g.cs");
        Assert.Contains("instance.Inner is not null", outerValidator, StringComparison.Ordinal);
    }
}
