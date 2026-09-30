using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate(StopOnFirstFailure = true)]
public sealed class AsyncRuleFailFastModel
{
    [UniqueName]
    public string? First { get; set; }

    [UniqueName]
    public string? Second { get; set; }
}
