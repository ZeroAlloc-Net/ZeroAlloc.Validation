using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

// The third level under ClusterOptions and NodeOptions, issue #246.
[Validate]
public class TlsOptions
{
    [GreaterThan(0)] public int Version { get; set; } = 1;
}
