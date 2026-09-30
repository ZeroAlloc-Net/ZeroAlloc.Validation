using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

// A generic options section, issue #238. It gets no ValidateWithZeroAlloc() overload of its own
// in this release; its closings are registered by the options models holding them.
[Validate]
public class EndpointOptions<TSecurity> where TSecurity : class, new()
{
    [NotEmpty] public string Url { get; set; } = "";

    [NotNull] public TSecurity? Security { get; set; } = new();
}
