using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocFlatModel
{
    [NotEmpty]
    public string Name { get; set; } = "";

    [GreaterThan(0)]
    public int Age { get; set; }
}
