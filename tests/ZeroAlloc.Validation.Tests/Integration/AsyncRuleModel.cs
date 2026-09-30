using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public sealed class AsyncRuleModel
{
    // Sync, async, sync: the failures come back in declaration order.
    [MinLength(6)]
    [UniqueName]
    [MaxLength(3)]
    public string? Name { get; set; }

    [UniqueName(When = nameof(ShouldCheckAlias))]
    public string? Alias { get; set; }

    [UniqueName(Unless = nameof(ShouldSkipCode))]
    public string? Code { get; set; }

    [StopOnFirstFailure]
    [MinLength(6)]
    [UniqueName]
    public string? Handle { get; set; }

    [UniqueName(Message = "{PropertyName} is in use.", ErrorCode = "IN_USE", Severity = Severity.Warning)]
    public string? Nickname { get; set; }

    public bool CheckAlias { get; set; }
    public bool SkipCode { get; set; }

    public bool ShouldCheckAlias() => CheckAlias;
    public bool ShouldSkipCode() => SkipCode;
}
