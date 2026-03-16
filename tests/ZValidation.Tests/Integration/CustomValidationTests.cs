using System.Linq;
using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class CustomValidationTests
{
    private readonly CustomValidationModelValidator _validator = new();

    [Fact]
    public void CustomValidation_ConditionMet_ReturnsCustomFailure()
    {
        var model = new CustomValidationModel { RequiresPromo = true, PromoCode = null };
        var result = _validator.Validate(model);
        ValidationAssert.HasErrorWithMessage(result, "PromoCode", "A promo code is required for this order.");
    }

    [Fact]
    public void CustomValidation_ConditionNotMet_NoCustomFailure()
    {
        var model = new CustomValidationModel { RequiresPromo = false, PromoCode = null };
        var result = _validator.Validate(model);
        Assert.DoesNotContain(result.Failures.ToArray(), f => string.Equals(f.PropertyName, "PromoCode", System.StringComparison.Ordinal));
    }

    [Fact]
    public void CustomValidation_ComposesWithPropertyRules()
    {
        // Both Reference (NotEmpty) and custom promo rule fail
        var model = new CustomValidationModel { Reference = "", RequiresPromo = true, PromoCode = null };
        var result = _validator.Validate(model);
        ValidationAssert.HasError(result, "Reference");
        ValidationAssert.HasError(result, "PromoCode");
    }

    [Fact]
    public void CustomValidation_AllValid_NoErrors()
    {
        var model = new CustomValidationModel { Reference = "REF01", RequiresPromo = true, PromoCode = "PROMO10" };
        var result = _validator.Validate(model);
        ValidationAssert.NoErrors(result);
    }
}
