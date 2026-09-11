using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

/// <summary>
/// Model-level fail-fast where every property group can yield at most one failure:
/// <see cref="PlayerId"/> carries a single rule, <see cref="Region"/> carries several
/// but short-circuits per-property. Both shapes take the direct-return path.
/// </summary>
[Validate(StopOnFirstFailure = true)]
public sealed class BenchFailFastRequest
{
    [NotEmpty]
    public string? PlayerId { get; set; }

    [StopOnFirstFailure]
    [NotEmpty]
    [MinLength(3)]
    [MaxLength(32)]
    public string Region { get; set; } = "";
}
