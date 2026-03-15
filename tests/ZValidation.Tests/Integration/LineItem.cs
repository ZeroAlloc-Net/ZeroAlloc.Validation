using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class LineItem
{
    [NotEmpty(Message = "SKU is required.")]
    public string Sku { get; set; } = "";

    [GreaterThan(0, Message = "Quantity must be positive.")]
    public int Quantity { get; set; }
}
