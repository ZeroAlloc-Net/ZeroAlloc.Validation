using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class DecimalModel
{
    [PrecisionScale(5, 2, Message = "Amount precision exceeded.")]
    public decimal Amount { get; set; }
}
