using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Class-level cascade plus model-level fail-fast: the first failing rule, and nothing else.</summary>
[Validate(StopOnFirstFailure = true)]
[StopOnFirstFailure]
public class ClassCascadeModel
{
    [NotEmpty]
    [MinLength(3)]
    public string? Tenant { get; set; }

    [NotEmpty]
    [MinLength(3)]
    public string? User { get; set; }
}
