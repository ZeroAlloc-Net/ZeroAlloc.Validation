using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

[Validate]
public class BenchCart
{
    [NotEmpty] public string             CartId { get; set; } = "";
    public     List<BenchLineItem>       Items  { get; set; } = [];
}
