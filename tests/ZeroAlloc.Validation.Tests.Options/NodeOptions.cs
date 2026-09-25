using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

// Composed by ClusterOptions and composing TlsOptions, three levels in all; neither is
// registered on its own, issue #246.
[Validate]
public class NodeOptions
{
    [GreaterThan(0)] public int Port { get; set; }

    public TlsOptions Tls { get; set; } = new();
}
