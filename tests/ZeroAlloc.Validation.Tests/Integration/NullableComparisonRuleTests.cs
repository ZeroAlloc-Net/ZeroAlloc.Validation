using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Comparison rules constrain a value that is present. A null passes every one of them, whatever
/// the range, the same split the length rules and <c>[IsInEnum]</c> follow: rejecting null is
/// <c>[NotNull]</c>'s job. They used to read a null <c>Nullable&lt;T&gt;</c> as 0, so
/// <c>[GreaterThan(0)]</c> rejected a missing value while <c>[LessThan(5)]</c> accepted it. See #276.
/// </summary>
public class NullableComparisonRuleTests
{
    public static TheoryData<string> Properties => new()
    {
        nameof(NullableComparisonModel.GreaterThanInt),
        nameof(NullableComparisonModel.GreaterThanLong),
        nameof(NullableComparisonModel.GreaterThanShort),
        nameof(NullableComparisonModel.GreaterThanByte),
        nameof(NullableComparisonModel.GreaterThanUInt),
        nameof(NullableComparisonModel.GreaterThanULong),
        nameof(NullableComparisonModel.GreaterThanFloat),
        nameof(NullableComparisonModel.GreaterThanDouble),
        nameof(NullableComparisonModel.GreaterThanDecimal),
        nameof(NullableComparisonModel.GreaterThanEnum),
        nameof(NullableComparisonModel.GreaterThanOrEqualToInt),
        nameof(NullableComparisonModel.LessThanLong),
        nameof(NullableComparisonModel.LessThanInt),
        nameof(NullableComparisonModel.LessThanOrEqualToDouble),
        nameof(NullableComparisonModel.InclusiveBetweenDecimal),
        nameof(NullableComparisonModel.ExclusiveBetweenFloat),
        nameof(NullableComparisonModel.EqualInt),
        nameof(NullableComparisonModel.NotEqualInt),
        nameof(NullableComparisonModel.EqualString),
        nameof(NullableComparisonModel.PrecisionScaleDecimal),
        nameof(NullableComparisonModel.EnumName),
    };

    private static readonly NullableComparisonModelValidator Validator = new();

    [Theory]
    [MemberData(nameof(Properties))]
    public void NullValue_Passes(string property)
    {
        var result = Validator.Validate(new NullableComparisonModel());

        Assert.DoesNotContain(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, property, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Properties))]
    public void InvalidValue_StillFails(string property)
    {
        var result = Validator.Validate(With(property, valid: false));

        Assert.Collection(result.Failures.ToArray(),
            f => Assert.Equal(property, f.PropertyName));
    }

    [Theory]
    [MemberData(nameof(Properties))]
    public void ValidValue_Passes(string property)
    {
        var result = Validator.Validate(With(property, valid: true));

        Assert.True(result.IsValid,
            string.Join("; ", result.Failures.ToArray().Select(f => f.ErrorMessage)));
    }

    [Fact]
    public void NullValue_WithNotNull_ReportsOnceFromNotNull()
    {
        var result = new NullableComparisonNotNullModelValidator()
            .Validate(new NullableComparisonNotNullModel());

        Assert.Collection(result.Failures.ToArray(),
            f =>
            {
                Assert.Equal("Quantity", f.PropertyName);
                Assert.Equal("Quantity must not be null.", f.ErrorMessage);
            },
            f =>
            {
                Assert.Equal("Rating", f.PropertyName);
                Assert.Equal("Rating must not be null.", f.ErrorMessage);
            });
    }

    [Fact]
    public void NonNullValue_WithNotNull_StillCheckedByTheComparison()
    {
        var result = new NullableComparisonNotNullModelValidator()
            .Validate(new NullableComparisonNotNullModel { Quantity = 0, Rating = 11m });

        Assert.Collection(result.Failures.ToArray(),
            f => Assert.Equal("Quantity must be greater than 0.", f.ErrorMessage),
            f => Assert.Equal("Rating must be between 1 and 10.", f.ErrorMessage));
    }

    // Sets one property and leaves the rest null, so a failure can only come from that property.
    private static readonly Dictionary<string, (Action<NullableComparisonModel> Valid, Action<NullableComparisonModel> Invalid)> Setters = new(StringComparer.Ordinal)
    {
        [nameof(NullableComparisonModel.GreaterThanInt)] = (m => m.GreaterThanInt = 1, m => m.GreaterThanInt = 0),
        [nameof(NullableComparisonModel.GreaterThanLong)] = (m => m.GreaterThanLong = 1, m => m.GreaterThanLong = -1),
        [nameof(NullableComparisonModel.GreaterThanShort)] = (m => m.GreaterThanShort = 1, m => m.GreaterThanShort = 0),
        [nameof(NullableComparisonModel.GreaterThanByte)] = (m => m.GreaterThanByte = 1, m => m.GreaterThanByte = 0),
        [nameof(NullableComparisonModel.GreaterThanUInt)] = (m => m.GreaterThanUInt = 1u, m => m.GreaterThanUInt = 0u),
        [nameof(NullableComparisonModel.GreaterThanULong)] = (m => m.GreaterThanULong = 1ul, m => m.GreaterThanULong = 0ul),
        [nameof(NullableComparisonModel.GreaterThanFloat)] = (m => m.GreaterThanFloat = 0.5f, m => m.GreaterThanFloat = 0f),
        [nameof(NullableComparisonModel.GreaterThanDouble)] = (m => m.GreaterThanDouble = 0.5, m => m.GreaterThanDouble = -0.5),
        [nameof(NullableComparisonModel.GreaterThanDecimal)] = (m => m.GreaterThanDecimal = 0.01m, m => m.GreaterThanDecimal = 0m),
        [nameof(NullableComparisonModel.GreaterThanEnum)] = (m => m.GreaterThanEnum = TrafficLight.Green, m => m.GreaterThanEnum = TrafficLight.Red),
        [nameof(NullableComparisonModel.GreaterThanOrEqualToInt)] = (m => m.GreaterThanOrEqualToInt = 1, m => m.GreaterThanOrEqualToInt = 0),
        [nameof(NullableComparisonModel.LessThanLong)] = (m => m.LessThanLong = -2, m => m.LessThanLong = -1),
        [nameof(NullableComparisonModel.LessThanInt)] = (m => m.LessThanInt = 4, m => m.LessThanInt = 5),
        [nameof(NullableComparisonModel.LessThanOrEqualToDouble)] = (m => m.LessThanOrEqualToDouble = -1, m => m.LessThanOrEqualToDouble = 0),
        [nameof(NullableComparisonModel.InclusiveBetweenDecimal)] = (m => m.InclusiveBetweenDecimal = 10m, m => m.InclusiveBetweenDecimal = 10.5m),
        [nameof(NullableComparisonModel.ExclusiveBetweenFloat)] = (m => m.ExclusiveBetweenFloat = 9.5f, m => m.ExclusiveBetweenFloat = 10f),
        [nameof(NullableComparisonModel.EqualInt)] = (m => m.EqualInt = 5, m => m.EqualInt = 0),
        [nameof(NullableComparisonModel.NotEqualInt)] = (m => m.NotEqualInt = 1, m => m.NotEqualInt = 0),
        [nameof(NullableComparisonModel.EqualString)] = (m => m.EqualString = "expected", m => m.EqualString = "other"),
        [nameof(NullableComparisonModel.PrecisionScaleDecimal)] = (m => m.PrecisionScaleDecimal = 123.45m, m => m.PrecisionScaleDecimal = 1.999m),
        [nameof(NullableComparisonModel.EnumName)] = (m => m.EnumName = "Green", m => m.EnumName = "Blue"),
    };

    private static NullableComparisonModel With(string property, bool valid)
    {
        var model = new NullableComparisonModel();
        var (setValid, setInvalid) = Setters[property];
        (valid ? setValid : setInvalid)(model);
        return model;
    }
}
