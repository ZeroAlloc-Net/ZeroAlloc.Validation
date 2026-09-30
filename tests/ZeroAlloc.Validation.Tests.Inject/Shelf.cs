using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

// A non-generic model holding a closing of a generic one, issue #238.
[Validate]
public class Shelf
{
    [NotEmpty] public string Code { get; set; } = "";

    public Page<Dock> Page { get; set; } = new();
}
