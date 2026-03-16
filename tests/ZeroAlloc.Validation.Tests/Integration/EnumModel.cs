using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class EnumModel
{
    [IsInEnum]
    public TrafficLight Light { get; set; }
}
