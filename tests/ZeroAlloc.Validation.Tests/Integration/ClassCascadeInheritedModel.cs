using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>The class-level cascade also governs rules inherited from a base type.</summary>
[Validate]
[StopOnFirstFailure]
public class ClassCascadeInheritedModel : ClassCascadeBase
{
    [NotEmpty]
    [MinLength(3)]
    public string? Own { get; set; }
}
