using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// A <c>When</c> or <c>Unless</c> guard covers a rule's whole condition, #282. The generator used
/// to prepend the guard to the condition unparenthesised, so for a condition such as
/// <c>lower || upper</c> the <c>&amp;&amp;</c> bound only the lower bound and the upper bound
/// ignored the guard. Each rule runs on the flat path, <see cref="GuardedRuleModel"/>, on the
/// buffered path, <see cref="GuardedRuleNestedModel"/>, and as a nested model,
/// <see cref="GuardedRuleParent"/>.
/// </summary>
public class GuardedRuleTests
{
    /// <summary>
    /// Values each rule rejects, by the rule's property name without its <c>When</c> or
    /// <c>Unless</c> suffix. A range rule lists one value past each bound.
    /// </summary>
    private static readonly Dictionary<string, object?[]> InvalidValues = new(StringComparer.Ordinal)
    {
        ["InclusiveBetween"] = [1, 11],
        ["ExclusiveBetween"] = [2, 10],
        ["NullableInclusiveBetween"] = [1, 11],
        ["NullableExclusiveBetween"] = [2, 10],
        ["GreaterThan"] = [0],
        ["NullableGreaterThan"] = [0],
        ["NullableGreaterThanOrEqualTo"] = [0],
        ["NullableLessThan"] = [10],
        ["NullableLessThanOrEqualTo"] = [10],
        ["NullableEqual"] = [4],
        ["NullableNotEqual"] = [0],
        ["StringInclusiveBetween"] = ["1", "11"],
        ["StringExclusiveBetween"] = ["2", "10"],
        ["StringGreaterThan"] = ["0"],
        ["PrecisionScale"] = [123.456m],
        ["NullablePrecisionScale"] = [123.456m],
        ["Length"] = ["a", "abcdef"],
        ["MinLength"] = ["a"],
        ["MaxLength"] = ["abcdef"],
        ["ValueObjectLength"] = [new Username("a"), new Username("abcdef")],
        ["EqualText"] = ["no"],
        ["NotEqualText"] = ["bad"],
        ["IsEnumName"] = ["Purple"],
        ["IsInEnum"] = [(TrafficLight)99],
        ["NullableIsInEnum"] = [(TrafficLight)99],
        ["Matches"] = ["abc"],
        ["EmailAddress"] = ["nope"],
        ["NotEmptyString"] = [""],
        ["NotEmptyList"] = [new List<int>()],
        ["NotEmptyArray"] = [Array.Empty<int>()],
        ["NotEmptyGuid"] = [Guid.Empty, null],
        ["NotNull"] = [null],
        ["Null"] = ["x"],
        ["Empty"] = ["x"],
        ["Must"] = [3],
        ["NotBlank"] = [" "],
    };

    private static readonly string[] Shapes = ["Flat", "Buffered", "Nested"];

    public static TheoryData<string, string, int> Cases()
    {
        var data = new TheoryData<string, string, int>();
        foreach (var shape in Shapes)
        {
            foreach (var (rule, values) in InvalidValues)
            {
                for (int i = 0; i < values.Length; i++)
                    data.Add(shape, rule, i);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void When_False_RuleSkipped(string shape, string rule, int value) =>
        Assert.Empty(Failures(shape, guarded: false, rule + "When", InvalidValues[rule][value], out _));

    [Theory]
    [MemberData(nameof(Cases))]
    public void When_True_InvalidValueRejected(string shape, string rule, int value)
    {
        var failures = Failures(shape, guarded: true, rule + "When", InvalidValues[rule][value], out var path);
        Assert.Equal([path], failures);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Unless_True_RuleSkipped(string shape, string rule, int value) =>
        Assert.Empty(Failures(shape, guarded: true, rule + "Unless", InvalidValues[rule][value], out _));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Unless_False_InvalidValueRejected(string shape, string rule, int value)
    {
        var failures = Failures(shape, guarded: false, rule + "Unless", InvalidValues[rule][value], out var path);
        Assert.Equal([path], failures);
    }

    [Theory]
    [InlineData(typeof(GuardedRuleModel))]
    [InlineData(typeof(GuardedRuleNestedModel))]
    public void EveryGuardedRuleOnTheModel_HasInvalidValues(Type model)
    {
        var guarded = model.GetProperties()
            .Where(p => p.GetCustomAttributes<ValidationAttribute>().Any())
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal);
        var covered = InvalidValues.Keys
            .SelectMany(rule => new[] { rule + "When", rule + "Unless" })
            .Order(StringComparer.Ordinal);

        Assert.Equal(covered, guarded);
    }

    /// <summary>
    /// Validates a valid model of <paramref name="shape"/> with <paramref name="property"/> set to
    /// <paramref name="value"/>, and returns the property path of each failure.
    /// </summary>
    private static List<string> Failures(string shape, bool guarded, string property, object? value, out string path)
    {
        ValidationResult result;
        switch (shape)
        {
            case "Flat":
            {
                var model = new GuardedRuleModel { Guarded = guarded };
                Set(model, property, value);
                result = new GuardedRuleModelValidator().Validate(model);
                path = property;
                break;
            }
            case "Buffered":
            {
                var model = new GuardedRuleNestedModel { Guarded = guarded };
                Set(model, property, value);
                result = new GuardedRuleNestedModelValidator(new AddressValidator()).Validate(model);
                path = property;
                break;
            }
            case "Nested":
            {
                var child = new GuardedRuleModel { Guarded = guarded };
                Set(child, property, value);
                result = new GuardedRuleParentValidator(new GuardedRuleModelValidator())
                    .Validate(new GuardedRuleParent { Child = child });
                path = $"{nameof(GuardedRuleParent.Child)}.{property}";
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }

        var names = new List<string>();
        foreach (ref readonly var failure in result.Failures)
            names.Add(failure.PropertyName);
        return names;
    }

    private static void Set(object model, string property, object? value) =>
        model.GetType().GetProperty(property)!.SetValue(model, value);
}
