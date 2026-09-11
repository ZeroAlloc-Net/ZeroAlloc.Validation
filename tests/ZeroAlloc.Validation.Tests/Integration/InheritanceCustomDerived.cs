using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class InheritanceCustomDerived : InheritanceCustomBase
{
    [NotEmpty]
    public string? Owner { get; init; }
}
