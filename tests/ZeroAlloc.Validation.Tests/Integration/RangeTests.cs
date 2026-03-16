using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class RangeTests
{
    private readonly RangeModelValidator _rangeValidator = new();
    private readonly ExclusiveBetweenModelValidator _exclusiveValidator = new();

    [Fact]
    public void GreaterThanOrEqualTo_Boundary_Passes()
    {
        ValidationAssert.NoErrors(_rangeValidator.Validate(new RangeModel { Percentage = 0 }));
        ValidationAssert.NoErrors(_rangeValidator.Validate(new RangeModel { Percentage = 100 }));
    }

    [Fact]
    public void BelowMinimum_Fails()
    {
        ValidationAssert.HasError(_rangeValidator.Validate(new RangeModel { Percentage = -1 }), "Percentage");
    }

    [Fact]
    public void AboveMaximum_Fails()
    {
        ValidationAssert.HasError(_rangeValidator.Validate(new RangeModel { Percentage = 101 }), "Percentage");
    }

    [Fact]
    public void ExclusiveBetween_MiddleValue_Passes()
    {
        ValidationAssert.NoErrors(_exclusiveValidator.Validate(new ExclusiveBetweenModel { Value = 50 }));
    }

    [Fact]
    public void ExclusiveBetween_BoundaryValues_Fail()
    {
        ValidationAssert.HasError(_exclusiveValidator.Validate(new ExclusiveBetweenModel { Value = 0 }), "Value");
        ValidationAssert.HasError(_exclusiveValidator.Validate(new ExclusiveBetweenModel { Value = 100 }), "Value");
    }
}
