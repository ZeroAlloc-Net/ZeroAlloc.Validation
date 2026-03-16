using System.Collections.Generic;
using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class Cart
{
    [NotEmpty]
    public string CustomerId { get; set; } = "";

    public IList<LineItem> Items { get; set; } = [];
}
