using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocParentModel
{
    [NotEmpty]
    public string Name { get; set; } = "";

    public AllocChildModel Child { get; set; } = new();
}
