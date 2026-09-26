using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

// Composes Warehouse, which itself composes other validators, issue #246.
[Validate]
public class Site
{
    public Warehouse? Main { get; set; }
}
