using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class EnumNameModel
{
    [IsEnumName(typeof(TrafficLight))]
    public string LightName { get; set; } = "";
}
