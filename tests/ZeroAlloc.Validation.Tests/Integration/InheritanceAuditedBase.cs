using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Base without [Validate]: no validator is generated for it, but its rules still flow into derived types.</summary>
public abstract class InheritanceAuditedBase
{
    [NotEmpty]
    public string? ModifiedBy { get; init; }
}
