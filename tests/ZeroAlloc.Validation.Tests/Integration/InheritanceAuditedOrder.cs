using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class InheritanceAuditedOrder : InheritanceAuditedBase
{
    [GreaterThan(0)]
    public int Total { get; init; }
}
