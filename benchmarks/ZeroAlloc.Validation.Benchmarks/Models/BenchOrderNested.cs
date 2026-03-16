using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

[Validate]
public class BenchOrderNested
{
    [NotEmpty] public string        Reference       { get; set; } = "";
    [NotNull]  public BenchAddress? ShippingAddress { get; set; }
}
