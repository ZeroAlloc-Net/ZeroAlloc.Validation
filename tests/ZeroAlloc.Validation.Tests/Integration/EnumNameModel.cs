using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class EnumNameModel
{
    [IsEnumName(typeof(TrafficLight))]
    public string LightName { get; set; } = "";
}
