using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class LengthModel
{
    [Length(2, 10)]
    public string Name { get; set; } = "";
}
