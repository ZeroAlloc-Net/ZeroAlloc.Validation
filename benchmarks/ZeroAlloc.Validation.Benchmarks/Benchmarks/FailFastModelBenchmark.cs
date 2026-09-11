using BenchmarkDotNet.Attributes;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Benchmarks;

/// <summary>
/// The invalid path under <c>[Validate(StopOnFirstFailure = true)]</c>. Where a property group
/// can produce at most one failure, the generator returns the result array directly instead of
/// filling a scratch buffer and copying out of it — one allocation on failure rather than two.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class FailFastModelBenchmark
{
    private static readonly BenchFailFastRequestValidator _za = new();

    private static readonly BenchFailFastRequest _valid = new()
    {
        PlayerId = "player-0001",
        Region   = "eu-west"
    };

    // Fails on the first property: single-rule group, direct return.
    private static readonly BenchFailFastRequest _invalidFirst = new()
    {
        PlayerId = "",
        Region   = "eu-west"
    };

    // Fails on the second property: multi-rule group with per-property short-circuit.
    private static readonly BenchFailFastRequest _invalidSecond = new()
    {
        PlayerId = "player-0001",
        Region   = "x"
    };

    [Benchmark(Baseline = true)] public bool ZA_Valid()         => _za.Validate(_valid).IsValid;
    [Benchmark]                  public bool ZA_InvalidFirst()  => _za.Validate(_invalidFirst).IsValid;
    [Benchmark]                  public bool ZA_InvalidSecond() => _za.Validate(_invalidSecond).IsValid;
}
