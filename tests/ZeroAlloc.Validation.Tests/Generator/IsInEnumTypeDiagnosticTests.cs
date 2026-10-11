using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// <c>[IsInEnum]</c> checks the value with <c>Enum.IsDefined</c>, which takes an enum. On any
/// other type the generated validator compiled and then threw ArgumentException on every call. It
/// is now reported as ZV0039 and left out. See #318.
/// </summary>
public class IsInEnumTypeDiagnosticTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("int?")]
    [InlineData("long")]
    [InlineData("string")]
    [InlineData("string?")]
    [InlineData("object")]
    [InlineData("System.Enum")]
    [InlineData("System.Enum?")]
    [InlineData("Point")]
    [InlineData("Point?")]
    [InlineData("Color[]")]
    [InlineData("System.Collections.Generic.List<Color>")]
    [InlineData("System.Collections.Generic.IEnumerable<Color?>")]
    public void NonEnumType_ReportsZV0039_AndLeavesTheRuleOut(string type)
    {
        var (result, output) = Run(type);

        var diagnostic = SingleZV0039(result);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("IsInEnum", SpanText(diagnostic));

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.DoesNotContain("IsDefined", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("instance.Code", generated, StringComparison.Ordinal);
        Assert.Contains("instance.Name", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Theory]
    [InlineData("Color", "!global::System.Enum.IsDefined<global::TestModels.Color>(instance.Code)")]
    [InlineData("Color?", "instance.Code.HasValue && !global::System.Enum.IsDefined<global::TestModels.Color>(instance.Code.Value)")]
    public void EnumType_ReportsNothing_AndEmitsTheRule(string type, string condition)
    {
        var (result, output) = Run(type);

        Assert.DoesNotContain(result.Diagnostics, IsZV0039);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.Contains(condition, generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Fact]
    public void Message_NamesTheRuleThePropertyAndItsType()
    {
        var (result, _) = Run("int?");

        Assert.Equal(
            "'IsInEnumAttribute' checks that 'Code' is a defined enum member, but its type 'int?' is not an enum; "
            + "declare the property as an enum or a nullable enum, or use [Must] or a custom ValidationAttribute<T>",
            SingleZV0039(result).GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RecordPrimaryConstructorProperty_IsChecked()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;
            public enum Color { Red, Green, Blue }
            [Validate]
            public record Foo([property: IsInEnum] int Code, [property: IsInEnum] Color Hue);
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        var diagnostic = SingleZV0039(result);
        Assert.Contains("'Code'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.DoesNotContain("instance.Code", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Enum.IsDefined<global::TestModels.Color>(instance.Hue)", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
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
                public enum Color { Red, Green, Blue }
                [ZeroAlloc.ValueObjects.ValueObject]
                public readonly struct Code { public int Value { get; init; } }
                [ZeroAlloc.ValueObjects.ValueObject]
                public readonly struct Hue { public Color Value { get; init; } }
                [Validate]
                public class Foo
                {
                    [IsInEnum] public Code Number { get; set; }
                    [IsInEnum] public Hue Tint { get; set; }
                }
            }
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        var message = SingleZV0039(result).GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("'Number'", message, StringComparison.Ordinal);
        Assert.Contains("'int'", message, StringComparison.Ordinal);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");
        Assert.DoesNotContain("instance.Number", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Enum.IsDefined<global::TestModels.Color>(instance.Tint.Value)", generated, StringComparison.Ordinal);
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
                [IsInEnum] public int Status { get; set; }
            }
            [Validate]
            public class RushOrder : Order
            {
                [NotNull] public string? Courier { get; set; }
            }
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);

        SingleZV0039(result);
        var derived = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.RushOrderValidator.g.cs");
        Assert.DoesNotContain("instance.Status", derived, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Theory]
    [InlineData("where T : struct, System.Enum", "T")]
    [InlineData("where T : struct, System.Enum", "T?")]
    public void TypeParameterConstrainedToAnEnum_ReportsNothing(string constraint, string type)
    {
        var (result, output) = RunGeneric(constraint, type);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "ZV0036" or "ZV0039");
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator`1.g.cs");
        Assert.Contains("global::System.Enum.IsDefined<T>(instance.Code", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    // A type parameter without struct, Enum has no form that compiles for every closing; that is
    // ZV0036's report, and ZV0039 does not report it a second time.
    [Theory]
    [InlineData("")]
    [InlineData("where T : System.Enum")]
    [InlineData("where T : struct")]
    public void TypeParameterNotConstrainedToAnEnum_ReportsZV0036Only(string constraint)
    {
        var (result, output) = RunGeneric(constraint, "T");

        Assert.DoesNotContain(result.Diagnostics, IsZV0039);
        Assert.Equal(1, result.Diagnostics.Count(d => string.Equals(d.Id, "ZV0036", StringComparison.Ordinal)));
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator`1.g.cs");
        Assert.DoesNotContain("IsDefined", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    private static (GeneratorDriverRunResult Result, Compilation Output) Run(string type)
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;
            public enum Color { Red, Green, Blue }
            public struct Point { public int X; public int Y; }
            [Validate]
            public class Foo
            {
                [IsInEnum] public {{type}} Code { get; set; } = default!;
                [NotNull] public string? Name { get; set; }
            }
            """;
        return GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);
    }

    private static (GeneratorDriverRunResult Result, Compilation Output) RunGeneric(string constraint, string type)
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class Foo<T> {{constraint}}
            {
                [IsInEnum] public {{type}} Code { get; set; } = default!;
            }
            """;
        return GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);
    }

    private static bool IsZV0039(Diagnostic d) => string.Equals(d.Id, "ZV0039", StringComparison.Ordinal);

    private static Diagnostic SingleZV0039(GeneratorDriverRunResult result)
    {
        Assert.Equal(1, result.Diagnostics.Count(IsZV0039));
        return result.Diagnostics.First(IsZV0039);
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
