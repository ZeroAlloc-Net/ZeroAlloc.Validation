using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public class ClassCascadeBase
{
    [NotEmpty]
    [MinLength(3)]
    public string? Inherited { get; set; }
}
