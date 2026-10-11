using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocEnumModel
{
    [IsInEnum]
    public TrafficLight Light { get; set; }

    [IsInEnum]
    public TrafficLight? Optional { get; set; }
}
