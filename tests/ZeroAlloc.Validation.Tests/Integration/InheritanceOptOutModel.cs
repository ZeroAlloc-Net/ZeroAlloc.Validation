using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate(IncludeBaseProperties = false)]
public class InheritanceOptOutModel : InheritanceBaseModel
{
    [GreaterThan(0)]
    public int Quantity { get; init; }
}
