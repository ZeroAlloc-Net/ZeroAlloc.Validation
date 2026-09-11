using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocReadOnlyListCollectionModel
{
    [NotEmpty]
    public string Name { get; set; } = "";

    public IReadOnlyList<AllocChildModel> Items { get; set; } = [];
}
