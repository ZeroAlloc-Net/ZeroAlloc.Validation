using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

// Composed by Shipment, issue #246.
[Validate]
public class Parcel
{
    [GreaterThan(0)] public int Weight { get; set; }
}
