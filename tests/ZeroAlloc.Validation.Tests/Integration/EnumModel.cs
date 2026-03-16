using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class EnumModel
{
    [IsInEnum]
    public TrafficLight Light { get; set; }
}
