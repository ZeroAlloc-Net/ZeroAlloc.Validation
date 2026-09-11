using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public class InheritanceShadowBase
{
    [MinLength(5)]
    public virtual string Code { get; init; } = "";
}
