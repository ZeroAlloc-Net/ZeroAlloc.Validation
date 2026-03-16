using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class Cart
{
    [NotEmpty]
    public string CustomerId { get; set; } = "";

    public IList<LineItem> Items { get; set; } = [];
}
