using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

// Composed by ClusterOptions and never registered on its own, issue #246.
[Validate]
public class NodeOptions
{
    [GreaterThan(0)] public int Port { get; set; }
}
