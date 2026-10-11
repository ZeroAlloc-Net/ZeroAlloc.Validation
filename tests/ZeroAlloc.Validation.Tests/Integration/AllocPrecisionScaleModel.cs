using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocPrecisionScaleModel
{
    [PrecisionScale(9, 2)]
    public decimal Amount { get; set; }

    [PrecisionScale(9, 2)]
    public decimal? Optional { get; set; }
}
