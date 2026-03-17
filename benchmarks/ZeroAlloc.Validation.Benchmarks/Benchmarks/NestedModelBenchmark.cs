using BenchmarkDotNet.Attributes;
using ZeroAlloc.Validation.Benchmarks.Models;
using ZeroAlloc.Validation.Benchmarks.Validators;

namespace ZeroAlloc.Validation.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class NestedModelBenchmark
{
    private static readonly BenchOrderNestedValidator   _za = new(new BenchAddressValidator());
    private static readonly FVBenchOrderNestedValidator _fv = new();

    private static readonly BenchOrderNested _valid = new()
    {
        Reference       = "ORD-2026-001",
        ShippingAddress = new BenchAddress
        {
            Street     = "123 Main St",
            City       = "Springfield",
            PostalCode = "12345"
        }
    };

    private static readonly BenchOrderNested _invalid = new()
    {
        Reference       = "",
        ShippingAddress = new BenchAddress
        {
            Street     = "",
            City       = "",
            PostalCode = ""
        }
    };

    [Benchmark]                    public bool ZA_Valid()   => _za.Validate(_valid).IsValid;
    [Benchmark]                    public bool ZA_Invalid() => _za.Validate(_invalid).IsValid;
    [Benchmark(Baseline = true)]   public bool FV_Valid()   => _fv.Validate(_valid).IsValid;
    [Benchmark]                    public bool FV_Invalid() => _fv.Validate(_invalid).IsValid;
}
