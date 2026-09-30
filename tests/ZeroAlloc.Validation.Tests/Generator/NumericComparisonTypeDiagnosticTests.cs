using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// A numeric comparison rule compares the value as <c>System.Convert.ToDouble(value)</c>. On a type
/// that conversion cannot handle, such as <c>DateTime</c>, <c>DateOnly</c> or <c>Guid</c>, the
/// generated validator compiled and then threw InvalidCastException for every value. It is now
/// reported as ZV0033 and left out. See #279.
/// </summary>
public class NumericComparisonTypeDiagnosticTests
{
    private static readonly string[] Rules =
    [
        "GreaterThan(0)", "GreaterThanOrEqualTo(0)", "LessThan(0)", "LessThanOrEqualTo(0)",
        "InclusiveBetween(0, 1)", "ExclusiveBetween(0, 1)", "Equal(0)", "NotEqual(0)",
    ];

    public static TheoryData<string, string> RulesOnUnconvertibleTypes()
    {
        string[] types =
        [
            "System.DateTime", "System.DateOnly", "System.TimeOnly", "System.Guid", "System.TimeSpan",
            "System.DateTimeOffset", "char", "object", "Point",
            "System.DateTime?", "System.DateOnly?", "System.TimeOnly?", "System.Guid?", "System.TimeSpan?",
            "char?", "Point?",
        ];
        return Combine(types);
    }

    public static TheoryData<string, string> RulesOnConvertibleTypes()
    {
        string[] types =
        [
            "int", "long", "short", "byte", "sbyte", "ushort", "uint", "ulong", "nint", "nuint",
            "float", "double", "decimal", "Color",
            "int?", "long?", "short?", "byte?", "sbyte?", "ushort?", "uint?", "ulong?", "nint?", "nuint?",
            "float?", "double?", "decimal?", "Color?",
            "string", "bool", "bool?",
        ];
        return Combine(types);
    }

    [Theory]
    [MemberData(nameof(RulesOnUnconvertibleTypes))]
    public void UnconvertibleType_ReportsZV0033_AndLeavesTheRuleOut(string rule, string type)
    {
        var (result, output) = Run(rule, type);

        var diagnostic = SingleZV0033(result);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(rule, SpanText(diagnostic));

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.DoesNotContain("instance.Amount", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Fact]
    public void Message_NamesTheRuleThePropertyAndItsType_AndPointsAtMust()
    {
        var (result, _) = Run("GreaterThan(0)", "System.DateOnly?");

        var diagnostic = SingleZV0033(result);
        Assert.Equal(
            "'GreaterThanAttribute' compares 'Amount' as a number, but its type 'System.DateOnly?' cannot be "
            + "converted to one; use [Must] or a custom ValidationAttribute<T> to compare it",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(RulesOnConvertibleTypes))]
    public void ConvertibleType_ReportsNothing_AndEmitsTheRule(string rule, string type)
    {
        var (result, output) = Run(rule, type);

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0033", StringComparison.Ordinal));
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.Contains("System.Convert.ToDouble(instance.Amount", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Theory]
    [InlineData("Equal(\"x\")")]
    [InlineData("NotEqual(\"x\")")]
    public void StringEquality_IsNotANumericComparison(string rule)
    {
        var (result, _) = Run(rule, "string");

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0033", StringComparison.Ordinal));
    }

    [Fact]
    public void NullableString_IsGuardedAgainstNull_NotReadAsZero()
    {
        var (result, output) = Run("GreaterThan(0)", "string?");

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.Contains(
            "instance.Amount is not null && (System.Convert.ToDouble(instance.Amount) <= 0)",
            generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Fact]
    public void OnlyTheComparisonIsLeftOut_OtherRulesOnTheModelStillRun()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class Foo
            {
                [NotNull, GreaterThan(0)] public System.DateTime? When { get; set; }
                [GreaterThan(0)] public int Count { get; set; }
            }
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        SingleZV0033(result);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.Contains("instance.When is null", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("instance.When.Value", generated, StringComparison.Ordinal);
        Assert.Contains("System.Convert.ToDouble(instance.Count) <= 0", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Fact]
    public void RuleOnAValidatedBaseType_IsReportedOnce_ByTheBaseValidator()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class Booking
            {
                [GreaterThan(0)] public System.DateOnly Arrival { get; set; }
            }
            [Validate]
            public class GroupBooking : Booking
            {
                [GreaterThan(0)] public int Guests { get; set; }
            }
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        SingleZV0033(result);
        var derived = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.GroupBookingValidator.g.cs");
        Assert.DoesNotContain("instance.Arrival", derived, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Fact]
    public void RuleOnAPlainBaseType_IsReportedByTheModel()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;
            public class Booking
            {
                [GreaterThan(0)] public System.DateOnly Arrival { get; set; }
            }
            [Validate]
            public class GroupBooking : Booking
            {
            }
            """;
        var (result, _) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        Assert.Contains("'Arrival'", SingleZV0033(result).GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void SinglePropertyValueObject_IsCheckedByTheTypeItUnwrapsTo()
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
                public readonly struct Deadline { public System.DateOnly Value { get; init; } }
                [ZeroAlloc.ValueObjects.ValueObject]
                public readonly struct Quantity { public int Value { get; init; } }
                [Validate]
                public class Foo
                {
                    [GreaterThan(0)] public Deadline Due { get; set; }
                    [GreaterThan(0)] public Quantity Count { get; set; }
                }
            }
            """;
        var (result, _) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        var diagnostic = SingleZV0033(result);
        Assert.Contains("'Due'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("'System.DateOnly'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static TheoryData<string, string> Combine(string[] types)
    {
        var data = new TheoryData<string, string>();
        foreach (var rule in Rules)
        {
            foreach (var type in types)
                data.Add(rule, type);
        }
        return data;
    }

    private static (GeneratorDriverRunResult Result, Compilation Output) Run(string rule, string type)
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;
            public enum Color { Red, Green, Blue }
            public struct Point { public int X; public int Y; }
            [Validate]
            public class Foo
            {
                [{{rule}}] public {{type}} Amount { get; set; } = default!;
            }
            """;
        return GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);
    }

    private static Diagnostic SingleZV0033(GeneratorDriverRunResult result)
    {
        static bool IsZV0033(Diagnostic d) => string.Equals(d.Id, "ZV0033", StringComparison.Ordinal);
        Assert.Equal(1, result.Diagnostics.Count(IsZV0033));
        return result.Diagnostics.First(IsZV0033);
    }

    private static string[] ProblemsInGeneratedCode(Compilation output)
    {
        var generatedTrees = output.SyntaxTrees.Skip(1).ToHashSet();
        return output.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning
                        && d.Location.SourceTree is { } tree && generatedTrees.Contains(tree))
            .Select(d => d.ToString())
            .ToArray();
    }

    private static string SpanText(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.ToString().Substring(
            diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
}
