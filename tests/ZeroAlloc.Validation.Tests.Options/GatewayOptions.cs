using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

// An options model holding a closing of a generic section, issue #238.
[Validate]
public class GatewayOptions
{
    [NotEmpty] public string Name { get; set; } = "";

    public EndpointOptions<TlsOptions> Upstream { get; set; } = new();
}
