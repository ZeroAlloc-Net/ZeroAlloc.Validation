using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class InheritanceShadowDerived : InheritanceShadowBase
{
    [MinLength(2)]
    public override string Code { get; init; } = "";
}
