using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class CustomValidationModel
{
    [NotEmpty]
    public string Reference { get; set; } = "ok";

    public string? PromoCode { get; set; }
    public bool RequiresPromo { get; set; }

    [CustomValidation]
    internal IEnumerable<ValidationFailure> ValidatePromoRule()
    {
        if (RequiresPromo && string.IsNullOrEmpty(PromoCode))
            yield return new ValidationFailure
            {
                PropertyName = nameof(PromoCode),
                ErrorMessage = "A promo code is required for this order."
            };
    }
}
