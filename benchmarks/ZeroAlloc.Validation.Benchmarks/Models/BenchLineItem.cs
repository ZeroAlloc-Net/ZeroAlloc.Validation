using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

[Validate]
public class BenchLineItem
{
    [NotEmpty]       public string Sku      { get; set; } = "";
    [GreaterThan(0)] public int    Quantity { get; set; }
}
