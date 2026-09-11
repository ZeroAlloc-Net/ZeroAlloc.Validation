using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class InheritanceDerivedModel : InheritanceBaseModel
{
    [GreaterThan(0)]
    public int Quantity { get; init; }
}
