using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// <c>[MinLength]</c>, <c>[MaxLength]</c> and <c>[Length]</c> read the value's <c>.Length</c>. On a
/// type without one, such as <c>List&lt;T&gt;</c> or <c>int</c>, the generated validator did not
/// compile, and the error pointed inside generated code. It is now reported as ZV0040 at the
/// attribute and the rule is left out. <c>Unit = TextLengthUnit.CodePoints</c> counts the code
/// points of a string, so on any other type it is reported the same way. See #361.
/// </summary>
public class LengthRuleTypeDiagnosticTests
{
    [Theory]
    [InlineData("MaxLength(3)", "int")]
    [InlineData("MaxLength(3)", "int?")]
    [InlineData("MinLength(3)", "System.Guid")]
    [InlineData("Length(1, 3)", "object")]
    [InlineData("MaxLength(3)", "System.Collections.Generic.List<int>")]
    [InlineData("MinLength(3)", "System.Collections.Generic.ICollection<int>?")]
    [InlineData("Length(1, 3)", "System.Collections.Generic.IEnumerable<int>")]
    [InlineData("MaxLength(3)", "Sized?")]
    [InlineData("MaxLength(3)", "HiddenLength")]
    [InlineData("MaxLength(3)", "NamedLength")]
    [InlineData("MaxLength(3)", "Pair")]
    public void TypeWithoutAReadableLength_ReportsZV0040_AndLeavesTheRuleOut(string rule, string type)
    {
        var (result, output) = Run(rule, type);

        var diagnostic = SingleZV0040(result);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(rule, SpanText(diagnostic));

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.DoesNotContain("instance.Value", generated, StringComparison.Ordinal);
        Assert.Contains("instance.Name", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Theory]
    [InlineData("MaxLength(3)", "string")]
    [InlineData("MaxLength(3)", "string?")]
    [InlineData("MinLength(3)", "int[]?")]
    [InlineData("Length(1, 3)", "char[]")]
    [InlineData("MaxLength(3)", "Sized")]
    [InlineData("MaxLength(3)", "LongSized")]
    [InlineData("Length(1, 3)", "ShortSized")]
    [InlineData("MinLength(3)", "DoubleSized")]
    [InlineData("MaxLength(3)", "FieldSized")]
    [InlineData("MaxLength(3)", "Code")]
    [InlineData("MaxLength(3, Unit = TextLengthUnit.CodePoints)", "string?")]
    [InlineData("MinLength(3, Unit = TextLengthUnit.CodePoints)", "string")]
    [InlineData("Length(1, 3, Unit = TextLengthUnit.CodePoints)", "Code")]
    [InlineData("MaxLength(3, Unit = TextLengthUnit.CodeUnits)", "int[]")]
    public void TypeWithAReadableLength_ReportsNothing_AndEmitsTheRule(string rule, string type)
    {
        var (result, output) = Run(rule, type);

        Assert.DoesNotContain(result.Diagnostics, IsZV0040);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.Contains("instance.Value", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Theory]
    [InlineData("MaxLength(3, Unit = TextLengthUnit.CodePoints)", "int[]")]
    [InlineData("MinLength(3, Unit = TextLengthUnit.CodePoints)", "char[]?")]
    [InlineData("Length(1, 3, Unit = TextLengthUnit.CodePoints)", "Sized")]
    [InlineData("MaxLength(3, Unit = TextLengthUnit.CodePoints)", "System.Text.StringBuilder")]
    public void CodePointsOnAValueThatIsNotAString_ReportsZV0040_AndLeavesTheRuleOut(string rule, string type)
    {
        var (result, output) = Run(rule, type);

        var diagnostic = SingleZV0040(result);
        Assert.Contains("TextLengthUnit.CodePoints", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.DoesNotContain("instance.Value", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Fact]
    public void Message_NamesTheRuleThePropertyAndItsType()
    {
        var (result, _) = Run("MaxLength(3)", "System.Collections.Generic.List<int>");

        Assert.Equal(
            "'MaxLengthAttribute' checks the length of 'Value', but its type 'System.Collections.Generic.List<int>' "
            + "has no Length the generated validator can read; use [Must] or a custom ValidationAttribute<T>",
            SingleZV0040(result).GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CodePointsMessage_NamesTheUnit()
    {
        var (result, _) = Run("MaxLength(3, Unit = TextLengthUnit.CodePoints)", "int[]");

        Assert.Equal(
            "'MaxLengthAttribute' checks the length of 'Value', but its type 'int[]' is not a string, and "
            + "Unit = TextLengthUnit.CodePoints counts the code points of a string; remove Unit, or use [Must] "
            + "or a custom ValidationAttribute<T>",
            SingleZV0040(result).GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RecordPrimaryConstructorProperty_IsChecked()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public record Foo([property: MaxLength(3)] System.Collections.Generic.List<int> Items, [property: MaxLength(3)] string Name);
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        Assert.Contains("'Items'", SingleZV0040(result).GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.DoesNotContain("instance.Items", generated, StringComparison.Ordinal);
        Assert.Contains("instance.Name.Length > 3", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Fact]
    public void RuleOnAValidatedBaseType_IsReportedOnce_ByTheBaseValidator()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class Order
            {
                [MinLength(1)] public int Count { get; set; }
            }
            [Validate]
            public class RushOrder : Order
            {
                [NotNull] public string? Courier { get; set; }
            }
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        SingleZV0040(result);
        var derived = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.RushOrderValidator.g.cs");
        Assert.DoesNotContain("instance.Count", derived, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    // A type parameter has no length rule form that compiles for every closing; that is ZV0036's
    // report, and ZV0040 does not report it a second time.
    [Theory]
    [InlineData("MaxLength(3)")]
    [InlineData("MaxLength(3, Unit = TextLengthUnit.CodePoints)")]
    public void TypeParameter_ReportsZV0036Only(string rule)
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class Foo<T>
            {
                [{{rule}}] public T Value { get; set; } = default!;
            }
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        Assert.DoesNotContain(result.Diagnostics, IsZV0040);
        Assert.Equal(1, result.Diagnostics.Count(d => string.Equals(d.Id, "ZV0036", StringComparison.Ordinal)));
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    private static (GeneratorDriverRunResult Result, Compilation Output) Run(string rule, string type)
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }
            namespace TestModels
            {
                public struct Sized { public int Length { get; set; } }
                public sealed class LongSized { public long Length { get; set; } }
                public struct FieldSized { public int Length; }
                public struct ShortSized { public ushort Length { get; set; } }
                public struct DoubleSized { public double Length { get; set; } }
                public sealed class HiddenLength { private int Length { get; set; } public int Size => Length; }
                public sealed class NamedLength { public string Length { get; set; } = ""; }
                [ZeroAlloc.ValueObjects.ValueObject]
                public readonly struct Code { public string Value { get; init; } }
                [ZeroAlloc.ValueObjects.ValueObject]
                public readonly struct Pair { public string First { get; init; } public string Second { get; init; } }
                [Validate]
                public class Foo
                {
                    [{{rule}}] public {{type}} Value { get; set; } = default!;
                    [NotNull] public string? Name { get; set; }
                }
            }
            """;
        return GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);
    }

    private static bool IsZV0040(Diagnostic d) => string.Equals(d.Id, "ZV0040", StringComparison.Ordinal);

    private static Diagnostic SingleZV0040(GeneratorDriverRunResult result)
    {
        Assert.Equal(1, result.Diagnostics.Count(IsZV0040));
        return result.Diagnostics.First(IsZV0040);
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
