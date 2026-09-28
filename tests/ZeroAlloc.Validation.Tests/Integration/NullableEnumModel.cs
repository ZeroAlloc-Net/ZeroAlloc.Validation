using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class NullableEnumModel
{
    [IsInEnum]
    public TrafficLight? Light { get; set; }
}
