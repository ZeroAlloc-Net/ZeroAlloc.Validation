using BenchmarkDotNet.Attributes;
using ZeroAlloc.Validation.Benchmarks.Models;
using ZeroAlloc.Validation.Benchmarks.Validators;

namespace ZeroAlloc.Validation.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class FlatModelBenchmark
{
    private static readonly BenchOrderValidator    _za = new();
    private static readonly FVBenchOrderValidator  _fv = new();

    private static readonly BenchOrder _valid = new()
    {
        Reference = "ORD-2026-001",
        Amount    = 99.99m,
        Email     = "customer@example.com"
    };

    private static readonly BenchOrder _invalid = new()
    {
        Reference = "",
        Amount    = -1m,
        Email     = "not-an-email"
    };

    [Benchmark]                    public bool ZA_Valid()   => _za.Validate(_valid).IsValid;
    [Benchmark]                    public bool ZA_Invalid() => _za.Validate(_invalid).IsValid;
    [Benchmark(Baseline = true)]   public bool FV_Valid()   => _fv.Validate(_valid).IsValid;
    [Benchmark]                    public bool FV_Invalid() => _fv.Validate(_invalid).IsValid;
}
