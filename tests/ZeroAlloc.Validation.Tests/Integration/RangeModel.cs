using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class RangeModel
{
    [GreaterThanOrEqualTo(0)]
    [LessThanOrEqualTo(100)]
    public int Percentage { get; set; }
}
