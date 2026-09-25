using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

// An options model whose validator composes others, issue #246.
[Validate]
public class ClusterOptions
{
    [NotEmpty] public string Name { get; set; } = "";

    public NodeOptions Leader { get; set; } = new();

    public IList<NodeOptions> Followers { get; set; } = new List<NodeOptions>();
}
