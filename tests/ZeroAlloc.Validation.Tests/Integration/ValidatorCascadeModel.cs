using ZValidation;

namespace ZValidation.Tests.Integration;

// Flat-path model (no nested validators)
[Validate(StopOnFirstFailure = true)]
public class ValidatorCascadeModel
{
    [NotEmpty]
    public string Reference { get; set; } = "ok";

    [GreaterThan(0)]
    public int Quantity { get; set; } = 1;
}
