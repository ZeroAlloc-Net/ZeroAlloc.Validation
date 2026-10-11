using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

public enum BenchPaymentStatus { Pending, Authorised, Settled }

[Validate]
public class BenchPayment
{
    [PrecisionScale(9, 2)]
    public decimal Amount { get; set; }

    [IsInEnum]
    public BenchPaymentStatus Status { get; set; }
}
