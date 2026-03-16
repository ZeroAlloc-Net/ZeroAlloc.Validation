using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class Order
{
    [NotEmpty]
    public string Reference { get; set; } = "";

    [NotNull]
    public Address? ShippingAddress { get; set; }

    // Address has [Validate] → automatically nested
    public Address BillingAddress { get; set; } = new();
}
