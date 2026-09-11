using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Every group carries one rule, so every group takes the direct-return path.</summary>
[Validate(StopOnFirstFailure = true)]
public sealed class FailFastSingleModel
{
    [NotEmpty(ErrorCode = "PLAYER_REQUIRED", Severity = Severity.Warning)]
    public string? PlayerId { get; set; }

    [GreaterThan(0)]
    public int Score { get; set; }

    [NotEmpty(When = nameof(NeedsRegion))]
    public string? Region { get; set; }

    public bool RequiresRegion { get; set; }

    public bool NeedsRegion() => RequiresRegion;
}
