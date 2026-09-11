using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocIListCollectionModel
{
    [NotEmpty]
    public string Name { get; set; } = "";

    public IList<AllocChildModel> Items { get; set; } = [];
}
