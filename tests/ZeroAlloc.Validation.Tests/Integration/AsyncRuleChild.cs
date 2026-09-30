using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public sealed class AsyncRuleChild
{
    [UniqueName]
    public string? Name { get; set; }
}
