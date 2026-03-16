using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class LengthModel
{
    [Length(2, 10)]
    public string Name { get; set; } = "";
}
