using ZValidation;

namespace ZValidation.Tests.Integration;

// Nested-path model (uses nested validator — Address already has [Validate])
[Validate(StopOnFirstFailure = true)]
public class ValidatorCascadeWithNestedModel
{
    [NotEmpty]
    public string Reference { get; set; } = "ok";

    public Address? ShippingAddress { get; set; }
}
