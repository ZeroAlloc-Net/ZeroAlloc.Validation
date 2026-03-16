using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class LineItem
{
    [NotEmpty(Message = "SKU is required.", ErrorCode = "SKU_REQUIRED")]
    public string Sku { get; set; } = "";

    [GreaterThan(0, Message = "Quantity must be positive.")]
    public int Quantity { get; set; }
}
