using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

// Models nested in other types with the same simple name, issue #207.
public static class Shipping
{
    [Validate]
    public class Request
    {
        [GreaterThan(0)] public int Parcels { get; set; }
    }
}
