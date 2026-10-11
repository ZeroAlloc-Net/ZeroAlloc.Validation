using BenchmarkDotNet.Attributes;
using ZeroAlloc.Validation.Benchmarks.Models;
using ZeroAlloc.Validation.Benchmarks.Validators;

namespace ZeroAlloc.Validation.Benchmarks.Benchmarks;

/// <summary>
/// <c>[PrecisionScale]</c> and <c>[IsInEnum]</c>, the two rules that allocated on the valid path
/// until issue #316 without any benchmark showing it.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class NumericRulesBenchmark
{
    private static readonly BenchPaymentValidator    _za = new();
    private static readonly FVBenchPaymentValidator  _fv = new();

    private static readonly BenchPayment _valid = new()
    {
        Amount = 1234.56m,
        Status = BenchPaymentStatus.Authorised,
    };

    private static readonly BenchPayment _invalid = new()
    {
        Amount = 1.234m,
        Status = (BenchPaymentStatus)42,
    };

    [Benchmark]                    public bool ZA_Valid()   => _za.Validate(_valid).IsValid;
    [Benchmark]                    public bool ZA_Invalid() => _za.Validate(_invalid).IsValid;
    [Benchmark(Baseline = true)]   public bool FV_Valid()   => _fv.Validate(_valid).IsValid;
    [Benchmark]                    public bool FV_Invalid() => _fv.Validate(_invalid).IsValid;
}
