using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocCollectionModel
{
    [NotEmpty]
    public string Name { get; set; } = "";

    // Declared as an array on purpose: interface-typed collections box the enumerator,
    // which is tracked separately.
    public AllocChildModel[] Items { get; set; } = [];
}
