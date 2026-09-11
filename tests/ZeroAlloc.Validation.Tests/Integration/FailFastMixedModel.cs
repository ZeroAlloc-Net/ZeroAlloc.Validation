using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// First group can yield one failure (direct return), the second can yield several
/// (no per-property stop) so it must keep using the buffer.
/// </summary>
[Validate(StopOnFirstFailure = true)]
public sealed class FailFastMixedModel
{
    [NotEmpty]
    public string? PlayerId { get; set; }

    [NotEmpty]
    [MinLength(5)]
    public string Region { get; set; } = "";
}
