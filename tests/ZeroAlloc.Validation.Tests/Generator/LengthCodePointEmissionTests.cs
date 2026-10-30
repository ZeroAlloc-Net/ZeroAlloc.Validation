using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// <c>Unit = TextLengthUnit.CodePoints</c> on a length rule counts code points instead of UTF-16
/// code units, #361. A string of <c>Length</c> code units holds between <c>Length / 2</c> and
/// <c>Length</c> code points, so the emitted check decides on <c>Length</c> alone wherever it can,
/// and counts only in the band where the two units disagree. ASCII and other BMP text far from
/// the limit costs the same comparison as a code-unit rule.
/// </summary>
public class LengthCodePointEmissionTests
{
    private const string Count = "global::ZeroAlloc.Validation.Internal.TextLength.CountCodePoints";

    [Theory]
    [InlineData("MaxLength(3)", "instance.Value.Length > 3")]
    [InlineData("MaxLength(3, Unit = TextLengthUnit.CodeUnits)", "instance.Value.Length > 3")]
    [InlineData("MinLength(3)", "instance.Value.Length < 3")]
    [InlineData("Length(2, 5)", "instance.Value.Length < 2 || instance.Value.Length > 5")]
    [InlineData("MaxLength(3, Unit = TextLengthUnit.CodePoints)",
        "instance.Value.Length > 3 && (instance.Value.Length > 6 || " + Count + "(instance.Value) > 3)")]
    [InlineData("MinLength(3, Unit = TextLengthUnit.CodePoints)",
        "instance.Value.Length < 3 || (instance.Value.Length < 6 && " + Count + "(instance.Value) < 3)")]
    [InlineData("Length(2, 5, Unit = TextLengthUnit.CodePoints)",
        "instance.Value.Length < 2 || instance.Value.Length > 10 || ((instance.Value.Length < 4 || instance.Value.Length > 5) && "
        + Count + "(instance.Value) is < 2 or > 5)")]
    public void Condition(string rule, string comparison)
    {
        var (generated, output) = Run(rule, "string?");

        Assert.Contains($"if (instance.Value is not null && ({comparison}))", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    // Twice the limit does not fit an int, so no string is long enough to be decided on Length
    // alone: the shortcut that needs it is left out rather than emitted as an overflowing constant.
    [Theory]
    [InlineData("MaxLength(1500000000, Unit = TextLengthUnit.CodePoints)",
        "instance.Value.Length > 1500000000 && " + Count + "(instance.Value) > 1500000000")]
    [InlineData("MinLength(1500000000, Unit = TextLengthUnit.CodePoints)",
        "instance.Value.Length < 1500000000 || " + Count + "(instance.Value) < 1500000000")]
    [InlineData("Length(1500000000, 2000000000, Unit = TextLengthUnit.CodePoints)",
        "instance.Value.Length < 1500000000 || " + Count + "(instance.Value) is < 1500000000 or > 2000000000")]
    [InlineData("Length(2, 2000000000, Unit = TextLengthUnit.CodePoints)",
        "instance.Value.Length < 2 || ((instance.Value.Length < 4 || instance.Value.Length > 2000000000) && "
        + Count + "(instance.Value) is < 2 or > 2000000000)")]
    public void LimitWhoseDoubleOverflows_LeavesThatShortcutOut(string rule, string comparison)
    {
        var (generated, output) = Run(rule, "string?");

        Assert.Contains($"if (instance.Value is not null && ({comparison}))", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    // A value object over a string reads its member, which is null in a default struct: the
    // length check is guarded on the string it reads, not on the struct, which cannot be null.
    [Theory]
    [InlineData("MaxLength(3)", "instance.Value.Value.Length > 3")]
    [InlineData("MaxLength(3, Unit = TextLengthUnit.CodePoints)",
        "instance.Value.Value.Length > 3 && (instance.Value.Value.Length > 6 || " + Count + "(instance.Value.Value) > 3)")]
    public void ValueObjectOverAString_IsGuardedOnTheString(string rule, string comparison)
    {
        var (generated, output) = Run(rule, "Code");

        Assert.Contains($"if (instance.Value.Value is not null && ({comparison}))", generated, StringComparison.Ordinal);
        Assert.Empty(ProblemsInGeneratedCode(output));
    }

    [Theory]
    [InlineData("MaxLength(3, Unit = TextLengthUnit.CodePoints)", "Value must not exceed 3 characters.")]
    [InlineData("MinLength(3, Unit = TextLengthUnit.CodePoints)", "Value must be at least 3 characters.")]
    [InlineData("Length(2, 5, Unit = TextLengthUnit.CodePoints)", "Value must be between 2 and 5 characters.")]
    [InlineData("Length(2, 5, Unit = TextLengthUnit.CodePoints, Message = \"{MinLength}-{MaxLength}\")", "\"2-5\"")]
    public void Message_IsTheSameAsForCodeUnits(string rule, string message)
    {
        var (generated, _) = Run(rule, "string?");

        Assert.Contains(message, generated, StringComparison.Ordinal);
    }

    private static (string Generated, Compilation Output) Run(string rule, string type)
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
                [ZeroAlloc.ValueObjects.ValueObject]
                public readonly struct Code { public string Value { get; init; } }
                [Validate]
                public class Foo
                {
                    [{{rule}}] public {{type}} Value { get; set; }
                }
            }
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        return (GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs"), output);
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
}
