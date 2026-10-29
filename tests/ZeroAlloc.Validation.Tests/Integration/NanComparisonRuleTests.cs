using System;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Every comparison with NaN is false, so a failure condition written as a disjunction of
/// comparisons never fired for it. The rules now negate the success condition. See #360.
/// </summary>
public class NanComparisonRuleTests
{
    [Fact] public void GreaterThan_RejectsNaN() =>
        Assert.False(Valid(m => m.GreaterThanDouble = double.NaN));

    [Fact] public void GreaterThanOrEqualTo_RejectsNaN() =>
        Assert.False(Valid(m => m.GreaterThanOrEqualToDouble = double.NaN));

    [Fact] public void LessThan_RejectsNaN() =>
        Assert.False(Valid(m => m.LessThanDouble = double.NaN));

    [Fact] public void LessThanOrEqualTo_RejectsNaN() =>
        Assert.False(Valid(m => m.LessThanOrEqualToDouble = double.NaN));

    [Fact] public void InclusiveBetween_RejectsNaN() =>
        Assert.False(Valid(m => m.InclusiveBetweenDouble = double.NaN));

    [Fact] public void ExclusiveBetween_RejectsFloatNaN() =>
        Assert.False(Valid(m => m.ExclusiveBetweenFloat = float.NaN));

    [Fact] public void InclusiveBetween_RejectsNullableNaN() =>
        Assert.False(Valid(m => m.InclusiveBetweenNullable = double.NaN));

    [Fact] public void GreaterThan_RejectsNullableFloatNaN() =>
        Assert.False(Valid(m => m.GreaterThanNullableFloat = float.NaN));

    [Fact] public void NullableNull_StillPasses() => Assert.True(Valid(_ => { }));

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.5, true)]
    [InlineData(1.0, true)]
    [InlineData(-0.1, false)]
    [InlineData(1.1, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(double.NegativeInfinity, false)]
    public void InclusiveBetween_OrderedValuesBehaveAsBefore(double value, bool valid) =>
        Assert.Equal(valid, Valid(m => m.InclusiveBetweenDouble = value));

    [Fact] public void GreaterThan_OrderedValuesBehaveAsBefore()
    {
        Assert.True(Valid(m => m.GreaterThanDouble = 0.1));
        Assert.False(Valid(m => m.GreaterThanDouble = 0));
    }

    private static bool Valid(Action<NanComparisonModel> set)
    {
        var model = new NanComparisonModel();
        set(model);
        return new NanComparisonModelValidator().Validate(model).IsValid;
    }
}
