using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public sealed class AsyncRuleCancellationModel
{
    [CancellationProbe]
    public string? Name { get; set; }
}
