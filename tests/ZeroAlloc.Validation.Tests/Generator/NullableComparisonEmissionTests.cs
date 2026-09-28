using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// A comparison rule on a <c>Nullable&lt;T&gt;</c> property checks the value only when one is present,
/// and compares the unwrapped value. It used to pass the <c>Nullable&lt;T&gt;</c> itself to
/// <c>Convert.ToDouble(object)</c>, which boxed on every call and read null as 0. See #276.
/// </summary>
public class NullableComparisonEmissionTests
{
    public static TheoryData<string, string> RulesAndTypes()
    {
        string[] rules =
        [
            "GreaterThan(0)", "GreaterThanOrEqualTo(0)", "LessThan(0)", "LessThanOrEqualTo(0)",
            "InclusiveBetween(0, 1)", "ExclusiveBetween(0, 1)", "Equal(0)", "NotEqual(0)",
        ];
        string[] types =
        [
            "int?", "long?", "short?", "byte?", "sbyte?", "ushort?", "uint?", "ulong?",
            "float?", "double?", "decimal?", "Color?",
            "System.DateTime?", "System.DateOnly?", "System.TimeOnly?",
        ];

        var data = new TheoryData<string, string>();
        foreach (var rule in rules)
        {
            foreach (var type in types)
                data.Add(rule, type);
        }

        data.Add("PrecisionScale(5, 2)", "decimal?");
        return data;
    }

    [Theory]
    [MemberData(nameof(RulesAndTypes))]
    public void NullableProperty_GuardsOnHasValue_AndComparesTheUnwrappedValue(string rule, string type)
    {
        var generated = GeneratedValidator(rule, type, out _);

        Assert.Contains("instance.Amount.HasValue && (", generated, StringComparison.Ordinal);
        Assert.Contains("instance.Amount.Value", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("(instance.Amount)", generated, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RulesAndTypes))]
    public void NullableProperty_GeneratedValidatorCompilesCleanly(string rule, string type)
    {
        GeneratedValidator(rule, type, out var output);

        var generatedTrees = output.SyntaxTrees.Skip(1).ToHashSet();
        var problems = output.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning
                        && d.Location.SourceTree is { } tree && generatedTrees.Contains(tree))
            .Select(d => d.ToString())
            .ToArray();

        Assert.Empty(problems);
    }

    [Fact]
    public void NonNullableProperty_IsNotGuarded()
    {
        var generated = GeneratedValidator("GreaterThan(0)", "int", out _);

        Assert.DoesNotContain("HasValue", generated, StringComparison.Ordinal);
        Assert.Contains("System.Convert.ToDouble(instance.Amount) <= 0", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueObjectOverANullable_GuardsTheUnwrappedValue()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }
            namespace TestModels
            {
                [ZeroAlloc.ValueObjects.ValueObject]
                public readonly struct Limit
                {
                    public int? Value { get; init; }
                }
                [Validate]
                public class Foo { [GreaterThan(0)] public Limit Amount { get; set; } }
            }
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");

        Assert.Contains(
            "instance.Amount.Value.HasValue && (System.Convert.ToDouble(instance.Amount.Value.Value) <= 0)",
            generated, StringComparison.Ordinal);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NullableStringProperty_EqualAndIsEnumName_AreGuardedAgainstNull()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;
            public enum Color { Red, Green, Blue }
            [Validate]
            public class Foo
            {
                [Equal("x")] public string? Code { get; set; }
                [IsEnumName(typeof(Color))] public string? ColorName { get; set; }
            }
            """;
        var (result, _) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");

        Assert.Contains("instance.Code is not null && (instance.Code != \"x\")", generated, StringComparison.Ordinal);
        Assert.Contains("instance.ColorName is not null && (!global::System.Enum.IsDefined(", generated, StringComparison.Ordinal);
    }

    private static string GeneratedValidator(string rule, string type, out Compilation output)
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;
            public enum Color { Red, Green, Blue }
            [Validate]
            public class Foo
            {
                [{{rule}}] public {{type}} Amount { get; set; }
            }
            """;
        var (result, compilation) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);
        output = compilation;
        return GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
    }
}
