using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

[Validate]
public class BenchOrder
{
    [NotEmpty]
    [MaxLength(50)]
    public string Reference { get; set; } = "";

    [GreaterThan(0)]
    public decimal Amount { get; set; }

    [NotEmpty]
    [EmailAddress]
    public string Email { get; set; } = "";
}
