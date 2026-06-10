using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

[Validate]
public sealed class Order
{
    [NotEmpty] public string CustomerName { get; set; } = "";
    public IReadOnlyList<OrderItem> Items { get; set; } = System.Array.Empty<OrderItem>();
    public IReadOnlyList<OrderTag> Tags { get; set; } = System.Array.Empty<OrderTag>();
}
