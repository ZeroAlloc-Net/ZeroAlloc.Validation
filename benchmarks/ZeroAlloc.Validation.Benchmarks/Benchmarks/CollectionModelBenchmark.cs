using BenchmarkDotNet.Attributes;
using ZeroAlloc.Validation.Benchmarks.Models;
using ZeroAlloc.Validation.Benchmarks.Validators;

namespace ZeroAlloc.Validation.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class CollectionModelBenchmark
{
    private static readonly BenchCartValidator    _za = new(new BenchLineItemValidator());
    private static readonly FVBenchCartValidator  _fv = new();

    private static readonly BenchCart _valid = new()
    {
        CartId = "CART-001",
        Items  =
        [
            new BenchLineItem { Sku = "SKU-A", Quantity = 2 },
            new BenchLineItem { Sku = "SKU-B", Quantity = 1 },
            new BenchLineItem { Sku = "SKU-C", Quantity = 5 }
        ]
    };

    private static readonly BenchCart _invalid = new()
    {
        CartId = "",
        Items  =
        [
            new BenchLineItem { Sku = "",      Quantity = -1 },
            new BenchLineItem { Sku = "SKU-B", Quantity =  0 },
            new BenchLineItem { Sku = "",      Quantity =  3 }
        ]
    };

    [Benchmark]                    public bool ZA_Valid()   => _za.Validate(_valid).IsValid;
    [Benchmark]                    public bool ZA_Invalid() => _za.Validate(_invalid).IsValid;
    [Benchmark(Baseline = true)]   public bool FV_Valid()   => _fv.Validate(_valid).IsValid;
    [Benchmark]                    public bool FV_Invalid() => _fv.Validate(_invalid).IsValid;
}
