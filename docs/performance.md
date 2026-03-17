---
id: performance
title: Performance
slug: /docs/performance
description: Zero allocation on the valid path — benchmark results comparing ZeroAlloc.Validation against FluentValidation 11.
sidebar_position: 9
---

# Performance

## Why zero allocation on the valid path

ZeroAlloc.Validation achieves zero allocation on the valid path through a lazy-allocation pattern driven by the source generator.

At compile time, the generator knows exactly how many rules exist for a given validator. It uses this information to emit a failure buffer with the following behaviour:

- **Valid path:** The buffer is never created. No rule is violated, so no buffer is allocated, and the result returns `Array.Empty<ValidationFailure>()` — a static singleton with no heap cost.
- **Invalid path:** A fixed-size array sized to the total number of rules is allocated exactly once, then trimmed to match the actual number of failures.

Because the generator resolves the rule count statically, there is no need for dynamic sizing or reallocation at runtime.

**Contrast with FluentValidation**, which allocates on every call regardless of whether validation passes:

- An internal `List<ValidationFailure>` is created on every invocation.
- Expression tree delegates are compiled and cached on first use, incurring upfront allocation.
- Value types are boxed when passed through generic pipelines.
- Various dictionary and collection structures are used internally for error accumulation.

These costs are unavoidable in FluentValidation's architecture because it has no compile-time knowledge of rule counts or model shapes.

## Benchmark environment

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
.NET SDK 10.0.104
  [Host]     : .NET 10.0.4 (10.0.426.12010), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.4 (10.0.426.12010), X64 RyuJIT AVX2
```

## Benchmark results — Flat model

Three directly validated properties: `[NotEmpty][MaxLength(50)]`, `[GreaterThan(0)]`, `[NotEmpty][EmailAddress]`.

| Method     | Mean         | Error      | StdDev      | Ratio | Allocated | Alloc Ratio |
|------------|-------------:|-----------:|------------:|------:|----------:|------------:|
| ZA_Valid   |     6.713 ns |  0.4350 ns |    1.255 ns |  0.02 |         - |        0.00 |
| ZA_Invalid |    44.012 ns |  2.7703 ns |    7.859 ns |  0.14 |     304 B |        0.46 |
| FV_Valid   |   327.269 ns | 10.4974 ns |   29.436 ns |  1.01 |     664 B |        1.00 |
| FV_Invalid | 2,462.893 ns | 75.0023 ns |  210.315 ns |  7.58 |    5408 B |        8.14 |

**Valid path:** ZeroAlloc is **~49x faster** and allocates **0 bytes** (vs 664 B).
**Invalid path:** ZeroAlloc is **~56x faster** and allocates **~18x less** (304 B vs 5,408 B).

## Benchmark results — Nested model

One scalar property plus a required nested object (3 child properties).

| Method     | Mean        | Error     | StdDev     | Ratio | Allocated | Alloc Ratio |
|------------|------------:|----------:|-----------:|------:|----------:|------------:|
| ZA_Valid   |    10.09 ns |  0.890 ns |   2.526 ns |  0.02 |         - |        0.00 |
| ZA_Invalid |    96.56 ns |  2.411 ns |   6.841 ns |  0.16 |     608 B |        0.41 |
| FV_Valid   |   619.14 ns | 12.334 ns |  32.707 ns |  1.00 |    1488 B |        1.00 |
| FV_Invalid | 2,974.10 ns | 99.618 ns | 280.974 ns |  4.82 |    6328 B |        4.25 |

**Valid path:** ZeroAlloc is **~61x faster** and allocates **0 bytes** (vs 1,488 B).
**Invalid path:** ZeroAlloc is **~31x faster** and allocates **~10x less** (608 B vs 6,328 B).

## Benchmark results — Collection model

A cart with a string ID and a list of three line items.

| Method     | Mean        | Error      | StdDev      | Ratio | Allocated | Alloc Ratio |
|------------|------------:|-----------:|------------:|------:|----------:|------------:|
| ZA_Valid   |    14.30 ns |   0.377 ns |    1.050 ns | 0.007 |         - |        0.00 |
| ZA_Invalid |   178.54 ns |   6.361 ns |   18.044 ns | 0.089 |     856 B |        0.25 |
| FV_Valid   | 2,042.95 ns |  89.644 ns |  254.305 ns | 1.014 |    3456 B |        1.00 |
| FV_Invalid | 5,957.29 ns | 249.827 ns |  704.642 ns | 2.958 |   11568 B |        3.35 |

**Valid path:** ZeroAlloc is **~143x faster** and allocates **0 bytes** (vs 3,456 B).
**Invalid path:** ZeroAlloc is **~33x faster** and allocates **~14x less** (856 B vs 11,568 B).

## Summary

| Scenario   | ZA valid  | FV valid   | Speedup | ZA alloc (valid) | FV alloc (valid) |
|------------|:---------:|:----------:|:-------:|:----------------:|:----------------:|
| Flat       |  6.7 ns   |  327 ns    |  ~49x   |       0 B        |      664 B       |
| Nested     | 10.1 ns   |  619 ns    |  ~61x   |       0 B        |    1,488 B       |
| Collection | 14.3 ns   | 2,043 ns   | ~143x   |       0 B        |    3,456 B       |

## Running the benchmarks yourself

```bash
# From repo root
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release --no-incremental
cd benchmarks/ZeroAlloc.Validation.Benchmarks
dotnet run -c Release -- --filter '*' --job Default
```

Note: `--no-incremental` is required because the source generator's output assembly is excluded from MSBuild's incremental-build inputs. Without it, a change to the generator may not be detected and stale generated code may be benchmarked.
