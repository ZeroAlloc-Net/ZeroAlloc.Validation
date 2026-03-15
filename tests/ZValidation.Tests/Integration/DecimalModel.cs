using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class DecimalModel
{
    [PrecisionScale(5, 2, Message = "Amount precision exceeded.")]
    public decimal Amount { get; set; }
}
