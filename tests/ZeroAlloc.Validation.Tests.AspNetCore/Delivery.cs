using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

// An action argument holding a closing of a generic model, issue #238.
[Validate]
public class Delivery
{
    public Crate<Parcel> Crate { get; set; } = new();
}
