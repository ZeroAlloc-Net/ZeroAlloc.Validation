using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Class-level cascade without model-level fail-fast: one failure per property, every property reported.</summary>
[Validate]
[StopOnFirstFailure]
public class ClassCascadeOnlyModel
{
    [NotEmpty]
    [MinLength(3)]
    public string? Tenant { get; set; }

    [NotEmpty]
    [MinLength(3)]
    public string? User { get; set; }
}
