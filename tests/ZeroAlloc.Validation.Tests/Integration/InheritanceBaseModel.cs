using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class InheritanceBaseModel
{
    [NotEmpty]
    public string? Name { get; init; }
}
