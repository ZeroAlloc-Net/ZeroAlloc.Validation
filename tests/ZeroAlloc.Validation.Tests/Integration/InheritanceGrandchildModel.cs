using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class InheritanceGrandchildModel : InheritanceDerivedModel
{
    [NotEmpty]
    public string? Sku { get; init; }
}
