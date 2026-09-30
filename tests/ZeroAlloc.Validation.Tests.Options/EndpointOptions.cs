using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

// A generic options section, issue #238. Its closings are registered by the options models
// holding them, and a closing used as an options model itself is validated through the
// generated ValidateWithZeroAlloc<TSecurity>() overload.
[Validate]
public class EndpointOptions<TSecurity> where TSecurity : class, new()
{
    [NotEmpty] public string Url { get; set; } = "";

    [NotNull] public TSecurity? Security { get; set; } = new();
}
