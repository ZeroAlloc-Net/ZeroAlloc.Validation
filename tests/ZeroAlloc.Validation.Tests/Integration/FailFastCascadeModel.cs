using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Multi-rule property with per-property short-circuiting — still at most one failure.</summary>
[Validate(StopOnFirstFailure = true)]
public sealed class FailFastCascadeModel
{
    [StopOnFirstFailure]
    [NotEmpty]
    [MinLength(3)]
    [MaxLength(8)]
    public string Region { get; set; } = "";

    [GreaterThan(0)]
    public int Score { get; set; }
}
