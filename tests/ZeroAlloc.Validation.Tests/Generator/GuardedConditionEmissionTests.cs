using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// A rule behind a <c>When</c> or <c>Unless</c> guard is emitted as <c>guards &amp;&amp; (condition)</c>,
/// the condition parenthesised as one operand. Several conditions have more than one term, such as
/// a range's <c>lower || upper</c>, and <c>&amp;&amp;</c> binds tighter than <c>||</c>, so an
/// unparenthesised condition left the upper bound outside the guard, #282.
/// </summary>
public class GuardedConditionEmissionTests
{
    private const string ValueObjectAttribute = """
        namespace ZeroAlloc.ValueObjects
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ValueObjectAttribute : System.Attribute { }
        }
        """;

    /// <summary>Every built-in rule, a <c>[Must]</c> and a custom rule, on the types that change its condition.</summary>
    public static readonly (string Rule, string Type)[] Rules =
    [
        ("NotNull", "string?"),
        ("NotEmpty", "string?"),
        ("NotEmpty", "int[]?"),
        ("NotEmpty", "System.Collections.Generic.List<int>?"),
        ("NotEmpty", "System.Collections.Generic.ICollection<int>?"),
        ("NotEmpty", "System.Collections.Generic.IEnumerable<int>?"),
        ("NotEmpty", "System.Guid"),
        ("NotEmpty", "System.Guid?"),
        ("MinLength(2)", "string?"),
        ("MaxLength(5)", "string?"),
        ("Length(2, 5)", "string?"),
        ("Length(2, 5)", "Code"),
        ("GreaterThan(0)", "int"),
        ("GreaterThan(0)", "int?"),
        ("GreaterThanOrEqualTo(0)", "int"),
        ("GreaterThanOrEqualTo(0)", "int?"),
        ("LessThan(0)", "int"),
        ("LessThan(0)", "int?"),
        ("LessThanOrEqualTo(0)", "int"),
        ("LessThanOrEqualTo(0)", "int?"),
        ("InclusiveBetween(2, 10)", "int"),
        ("InclusiveBetween(2, 10)", "int?"),
        ("ExclusiveBetween(2, 10)", "int"),
        ("ExclusiveBetween(2, 10)", "int?"),
        ("Equal(1)", "int"),
        ("Equal(1)", "int?"),
        ("NotEqual(1)", "int"),
        ("NotEqual(1)", "int?"),
        ("GreaterThan(0)", "string?"),
        ("InclusiveBetween(2, 10)", "string?"),
        ("ExclusiveBetween(2, 10)", "string?"),
        ("Equal(1)", "string?"),
        ("Equal(\"x\")", "string?"),
        ("NotEqual(\"x\")", "string?"),
        ("PrecisionScale(5, 2)", "decimal"),
        ("PrecisionScale(5, 2)", "decimal?"),
        ("EmailAddress", "string?"),
        ("Matches(\"^[0-9]+$\")", "string?"),
        ("Null", "string?"),
        ("Empty", "string?"),
        ("IsInEnum", "Color"),
        ("IsInEnum", "Color?"),
        ("IsEnumName(typeof(Color))", "string?"),
        ("Must(nameof(Check))", "int"),
        ("Positive", "int"),
    ];

    private static readonly string[] Guards = ["When", "Unless", "Both"];

    private static readonly string[] Paths = ["Flat", "Buffered"];

    public static TheoryData<string, string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var (rule, type) in Rules)
        {
            foreach (var guard in Guards)
            {
                foreach (var path in Paths)
                    data.Add(rule, type, guard, path);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GuardedRule_ParenthesisesTheWholeCondition(string rule, string type, string guard, string path)
    {
        var condition = RuleLine(rule, type, guard: null, path, out _);
        Assert.StartsWith("if (", condition, StringComparison.Ordinal);
        condition = condition["if (".Length..^1];

        var guards = guard switch
        {
            "When" => "instance.IsChecked() && ",
            "Unless" => "!instance.IsExempt() && ",
            _ => "instance.IsChecked() && !instance.IsExempt() && ",
        };

        Assert.Equal($"if ({guards}({condition}))", RuleLine(rule, type, guard, path, out _));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GuardedRule_GeneratedValidatorCompilesCleanly(string rule, string type, string guard, string path)
    {
        RuleLine(rule, type, guard, path, out var output);

        var generatedTrees = output.SyntaxTrees.Skip(2).ToHashSet();
        var problems = output.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning
                        && d.Location.SourceTree is { } tree && generatedTrees.Contains(tree))
            .Select(d => d.ToString())
            .ToArray();

        Assert.Empty(problems);
    }

    [Fact]
    public void UnguardedRule_ConditionIsNotParenthesisedAgain()
    {
        Assert.Equal(
            "if (System.Convert.ToDouble(instance.Amount) < 2 || System.Convert.ToDouble(instance.Amount) > 10)",
            RuleLine("InclusiveBetween(2, 10)", "int", guard: null, "Flat", out _));
    }

    [Fact]
    public void IssueRepro_WhenGuardCoversBothBounds()
    {
        Assert.Equal(
            "if (instance.IsChecked() && (System.Convert.ToDouble(instance.Amount) < 2 || System.Convert.ToDouble(instance.Amount) > 10))",
            RuleLine("InclusiveBetween(2, 10)", "int", "When", "Flat", out _));
    }

    [Fact]
    public void ValueObjectOverAString_LengthGuardCoversBothBounds()
    {
        // A struct value object cannot be null, so its length check is not null-guarded, and the
        // condition is a bare "shorter || longer" like a range's.
        Assert.Equal(
            "if (!instance.IsExempt() && (instance.Amount.Value.Length < 2 || instance.Amount.Value.Length > 5))",
            RuleLine("Length(2, 5)", "Code", "Unless", "Flat", out _));
    }

    /// <summary>
    /// The one line that opens the rule on <c>Foo.Amount</c>, trimmed, from a model on the flat or
    /// the buffered emission path. <paramref name="guard"/> is <c>When</c>, <c>Unless</c>, <c>Both</c>
    /// or <see langword="null"/> for none.
    /// </summary>
    private static string RuleLine(string rule, string type, string? guard, string path, out Compilation output)
    {
        var guardArgs = guard switch
        {
            null => "",
            "When" => "When = nameof(IsChecked)",
            "Unless" => "Unless = nameof(IsExempt)",
            _ => "When = nameof(IsChecked), Unless = nameof(IsExempt)",
        };
        var attribute = guardArgs.Length == 0 ? rule
            : rule.EndsWith(')') ? $"{rule[..^1]}, {guardArgs})"
            : $"{rule}({guardArgs})";
        var nested = string.Equals(path, "Buffered", StringComparison.Ordinal) ? "public Child Child { get; set; } = new();" : "";

        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;
            public enum Color { Red, Green, Blue }
            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly struct Code
            {
                public string Value { get; }
                public Code(string value) => Value = value;
            }
            public sealed class PositiveAttribute : ValidationAttribute<int>
            {
                public override bool IsValid(int value) => value > 0;
            }
            [Validate]
            public class Child { [NotNull] public string? Name { get; set; } = ""; }
            [Validate]
            public class Foo
            {
                public bool Checked { get; set; }
                public bool Exempt { get; set; }
                [{{attribute}}] public {{type}} Amount { get; set; }
                {{nested}}
                public bool IsChecked() => Checked;
                public bool IsExempt() => Exempt;
                public bool Check(int value) => value > 0;
            }
            """;

        var (result, compilation) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, extraSources: [ValueObjectAttribute], nullableContextOptions: NullableContextOptions.Enable);
        output = compilation;
        return FindRuleLine(GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs"), path);
    }

    /// <summary>The one line in <paramref name="generated"/> that opens the rule on <c>Foo.Amount</c>, trimmed.</summary>
    private static string FindRuleLine(string generated, string path)
    {
        // Not Assert.Single, which HLQ005 flags by name, a false positive, #212.
        var lines = generated.Split('\n').Select(l => l.Trim()).ToArray();
        var at = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].StartsWith("if (", StringComparison.Ordinal)
                        && lines[i].Contains("instance.Amount", StringComparison.Ordinal))
            .ToArray();
        Assert.True(at.Length == 1, $"Expected one rule line, got {at.Length}.");

        // The flat path, taken without a nested property, braces the rule's body; the buffered
        // path adds the failure straight to its buffer.
        var body = lines[at[0] + 1];
        if (string.Equals(path, "Buffered", StringComparison.Ordinal))
            Assert.StartsWith("_buf.Add(", body, StringComparison.Ordinal);
        else
            Assert.Equal("{", body);

        return lines[at[0]];
    }
}
