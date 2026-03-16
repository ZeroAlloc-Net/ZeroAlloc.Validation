using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

[Validate]
public class BenchAddress
{
    [NotEmpty] public string Street     { get; set; } = "";
    [NotEmpty] public string City       { get; set; } = "";
    [NotEmpty] public string PostalCode { get; set; } = "";
}
