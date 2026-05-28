using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

[Validate]
public sealed class OrderItem
{
    [NotEmpty(Message = "Sku is required.")]
    public string Sku { get; set; } = "";
}
