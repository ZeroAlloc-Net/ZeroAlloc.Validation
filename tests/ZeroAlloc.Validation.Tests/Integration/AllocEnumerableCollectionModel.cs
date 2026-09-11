using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocEnumerableCollectionModel
{
    [NotEmpty]
    public string Name { get; set; } = "";

    public IEnumerable<AllocChildModel> Items { get; set; } = [];
}
