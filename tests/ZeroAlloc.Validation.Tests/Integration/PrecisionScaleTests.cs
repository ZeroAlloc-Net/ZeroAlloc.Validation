using System.Linq;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class PrecisionScaleTests
{
    private readonly DecimalModelValidator _validator = new();

    [Fact]
    public void ValidDecimal_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new DecimalModel { Amount = 123.45m }));
    }

    [Fact]
    public void TooManyDecimalPlaces_FailsWithCustomMessage()
    {
        var result = _validator.Validate(new DecimalModel { Amount = 1.999m });
        ValidationAssert.HasError(result, "Amount");
        Assert.Equal("Amount precision exceeded.", result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Amount", StringComparison.Ordinal)).ErrorMessage);
    }

    [Fact]
    public void TooManyTotalDigits_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new DecimalModel { Amount = 1234.00m }), "Amount");
    }
}
