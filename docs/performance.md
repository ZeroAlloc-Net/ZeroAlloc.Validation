---
id: performance
title: Performance
slug: /docs/performance
description: Zero allocation on the valid path — benchmark results comparing ZeroAlloc.Validation against FluentValidation.
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

Measured 2026-10-15 against **FluentValidation 12.1.1**, the version the benchmark project
references, with BenchmarkDotNet's in-process toolchain (`--inProcess`):

```
BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
Intel Core Ultra 7 255H 3.69GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> **Timings are indicative** — measured on a developer laptop (Core Ultra 7 255H, hybrid P/E cores)
> under background load; allocations are exact, ratios are same-run. ZeroAlloc.Validation and
> FluentValidation were measured side by side in one BenchmarkDotNet run per model, so compare
> the ratios rather than absolute nanoseconds. An out-of-process job in the same run measured the
> ZeroAlloc invalid paths 1.6 to 2.4 times faster than the in-process figures below; see the
> [benchmark README](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/benchmarks/ZeroAlloc.Validation.Benchmarks/README.md#environment).

## Benchmark results — Flat model

Three directly validated properties: `[NotEmpty][MaxLength(50)]`, `[GreaterThan(0)]`, `[NotEmpty][EmailAddress]`.

| Method     | Mean         | Error      | StdDev     | Ratio | Allocated | Alloc Ratio |
|------------|-------------:|-----------:|-----------:|------:|----------:|------------:|
| ZA_Valid   |     4.198 ns |  0.0232 ns |  0.0206 ns |  0.04 |         - |        0.00 |
| ZA_Invalid |    45.212 ns |  0.4384 ns |  0.4101 ns |  0.39 |     120 B |        0.17 |
| FV_Valid   |   116.833 ns |  1.4367 ns |  1.1997 ns |  1.00 |     696 B |        1.00 |
| FV_Invalid | 1,232.737 ns | 14.6328 ns | 13.6875 ns | 10.55 |    5216 B |        7.49 |

**Valid path:** ZeroAlloc is **27.8x faster** and allocates **0 bytes** (vs 696 B).
**Invalid path:** ZeroAlloc is **27.3x faster** and allocates **43.5x less** (120 B vs 5,216 B).

## Benchmark results — Nested model

One scalar property plus a required nested object (3 child properties).

| Method     | Mean         | Error      | StdDev      | Ratio | Allocated | Alloc Ratio |
|------------|-------------:|-----------:|------------:|------:|----------:|------------:|
| ZA_Valid   |     2.704 ns |  0.0659 ns |   0.0584 ns | 0.008 |         - |        0.00 |
| ZA_Invalid |   145.443 ns |  2.5690 ns |   6.6771 ns | 0.411 |     488 B |        0.33 |
| FV_Valid   |   360.254 ns | 17.6492 ns |  52.0392 ns | 1.019 |    1488 B |        1.00 |
| FV_Invalid | 1,848.720 ns | 71.4433 ns | 202.6729 ns | 5.229 |    6032 B |        4.05 |

**Valid path:** ZeroAlloc is **133.2x faster** and allocates **0 bytes** (vs 1,488 B).
**Invalid path:** ZeroAlloc is **12.7x faster** and allocates **12.4x less** (488 B vs 6,032 B).

## Benchmark results — Collection model

A cart with a string ID and a list of three line items.

| Method     | Mean         | Error      | StdDev     | Ratio | Allocated | Alloc Ratio |
|------------|-------------:|-----------:|-----------:|------:|----------:|------------:|
| ZA_Valid   |     7.896 ns |  0.0493 ns |  0.0462 ns |  0.01 |         - |        0.00 |
| ZA_Invalid |   195.089 ns |  2.0402 ns |  1.9084 ns |  0.27 |     592 B |        0.17 |
| FV_Valid   |   723.962 ns | 13.8166 ns | 10.7871 ns |  1.00 |    3528 B |        1.00 |
| FV_Invalid | 2,735.001 ns | 14.6138 ns | 12.9548 ns |  3.78 |   11264 B |        3.19 |

**Valid path:** ZeroAlloc is **91.7x faster** and allocates **0 bytes** (vs 3,528 B).
**Invalid path:** ZeroAlloc is **14.0x faster** and allocates **19.0x less** (592 B vs 11,264 B).

## Benchmark results — Precision and enum rules

`[PrecisionScale(9, 2)]` on a decimal and `[IsInEnum]` on a concrete enum.

| Method     | Mean        | Error     | StdDev     | Ratio | Allocated | Alloc Ratio |
|------------|------------:|----------:|-----------:|------:|----------:|------------:|
| ZA_Valid   |    13.31 ns |  0.125 ns |   0.117 ns |  0.02 |         - |        0.00 |
| ZA_Invalid |    31.90 ns |  0.288 ns |   0.255 ns |  0.05 |      88 B |        0.11 |
| FV_Valid   |   625.32 ns | 14.737 ns |  41.565 ns |  1.00 |     816 B |        1.00 |
| FV_Invalid | 2,005.95 ns | 53.111 ns | 153.239 ns |  3.22 |    6593 B |        8.08 |

**Valid path:** ZeroAlloc is **47.0x faster** and allocates **0 bytes** (vs 816 B).
**Invalid path:** ZeroAlloc is **62.9x faster** and allocates **74.9x less** (88 B vs 6,593 B).

## Summary

<!-- BENCH:START -->
_Last refreshed: 2026-10-15, against FluentValidation 12.1.1. Timings are indicative — measured on a developer laptop (Core Ultra 7 255H, hybrid P/E cores) under background load; allocations are exact, ratios are same-run._

| Scenario | Speedup (valid) | Speedup (invalid) | ZA alloc (valid) | FV alloc (valid) | ZA alloc (invalid) | FV alloc (invalid) | ZA valid | FluentValidation 12 valid |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Flat | **27.8×** | 27.3× | **0 B** | 696 B | 120 B | 5,216 B | 4.198 ns | 116.833 ns |
| Nested | **133.2×** | 12.7× | **0 B** | 1,488 B | 488 B | 6,032 B | 2.704 ns | 360.254 ns |
| Collection (3 items) | **91.7×** | 14.0× | **0 B** | 3,528 B | 592 B | 11,264 B | 7.896 ns | 723.962 ns |
| Precision and enum | **47.0×** | 62.9× | **0 B** | 816 B | 88 B | 6,593 B | 13.31 ns | 625.32 ns |

ZeroAlloc.Validation is **27.8–133.2× faster** than FluentValidation 12 on the valid path with **zero heap allocation**. On the invalid path it is 12.7–62.9× faster and allocates 12.4–74.9× less. FluentValidation allocates on every call, valid or not, for its per-call `List<ValidationFailure>` and validation context.
<!-- BENCH:END -->

### Invalid-path figures in the benchmarks

The benchmark models fail more rules, and two of them nest, so they allocate more than the 56–120 B
in the table below. Each figure is exactly the result arrays plus the nested property paths:

- **Flat, 120 B:** one result array holding 3 failures, a 24-byte header plus 3 × 32 bytes.
- **Precision and enum, 88 B:** one result array holding 2 failures.
- **Nested, 488 B:** the child validator's 3-failure array, 120 B; the parent's 4-failure array,
  152 B; and the three path strings `ShippingAddress.Street`, `ShippingAddress.City` and
  `ShippingAddress.PostalCode`, 72, 64 and 80 B.
- **Collection, 592 B:** the three items' result arrays, 88, 56 and 56 B; the parent's 5-failure
  array, 184 B; and the four path strings such as `Items[0].Sku`, 48, 56, 56 and 48 B.

The 304, 608 and 856 B these benchmarks reported before were measured before the failure buffer
moved to `ArrayPool`, when the staging buffer itself was allocated on every failing call.

## Allocation on the invalid path

The valid path never allocates. The invalid path must allocate the result array itself, since a failure has to be handed back somehow, but nothing else needs allocating on the way there.

Failures are staged in a scratch buffer that rents from `ArrayPool<ValidationFailure>.Shared` and only on the first failure, so a validation that reports nothing never touches the pool at all. A failing validation therefore costs the result array and nothing else:

| model shape | valid | invalid |
|---|---:|---:|
| 1 rule, 1 failure | 0 B | 56 B |
| 3 properties, 1 failure | 0 B | 56 B |
| 3 properties, 3 failures | 0 B | 120 B |
| 1 property with 3 rules, 2 failures | 0 B | 88 B |
| inherited rules, 1 failure | 0 B | 56 B |

56 B is a one-element `ValidationFailure[]`: a 24-byte array header plus one 32-byte failure. Everything above it scales with the number of failures actually reported, not with the number of rules declared.

Under `[Validate(StopOnFirstFailure = true)]` there is a further shortcut. A property that can only ever produce one failure (a single rule, or several rules behind `[StopOnFirstFailure]`) returns its result array directly without staging anything, so the buffer is not even created. Where every property in the model is like that, no buffer is emitted at all. See [Model-level short-circuit](./advanced.md#validatestoponfirstfailure--true--model-level-short-circuit).

### Collection property types

How a collection property is declared decides how it is walked, and `foreach` over an interface would box its enumerator on every validation — valid path included. The generator avoids that where the type allows it:

| declared as | walked by | allocates |
|---|---|---:|
| `T[]` | `foreach` (already compiles to indexing) | 0 B |
| `List<T>` | span over the backing array | 0 B |
| `IList<T>` | indexer | 0 B |
| `IReadOnlyList<T>` | indexer | 0 B |
| `IEnumerable<T>` | `foreach` | 32 B |

`IEnumerable<T>` exposes no count and no indexer, so its enumerator is unavoidable. Prefer any of the other four for a collection that is validated on a hot path.

A `[CustomValidation]` method written with `yield return` also allocates its iterator state machine on every call, even when it yields nothing. Declaring it to return `ValidationFailure[]` or `ReadOnlySpan<ValidationFailure>` instead, and handing back an empty one when there is nothing to report, costs 0 B. See [choosing a return type](./custom-validation.md#which-return-type-to-use).

### Pipeline behaviors

A `[PipelineBehavior]` wraps the validation body in a chain of `next` delegates. On a model
without nested validators those delegates are `static` lambdas, which the compiler creates once,
so the chain allocates nothing.

On a model with a nested, collection or `[ValidateWith]` validator, the body reads those
validators through the validator's fields, so the delegates capture the validator and cannot be
`static`. The generated validator keeps each of them in a private field, created on the first
call, so from then on the chain allocates nothing either, generic models included. This covers
`Validate`, `ValidateAsync`, and the asynchronous validation of a model with asynchronous rules.

The cached delegates live as long as the validator. Two threads that make a validator's first call
together may each create one; they are equivalent, and one is kept. Before
[#298](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/298) each call allocated one
delegate per behavior, 64 B for one behavior on a 64-bit runtime, and before
[#294](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/294) such a validator did not
compile at all.

## Running the benchmarks yourself

```bash
# From repo root
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release --no-incremental
cd benchmarks/ZeroAlloc.Validation.Benchmarks
dotnet run -c Release -- --filter '*' --job Default --inProcess
```

Note: `--no-incremental` is required because the source generator's output assembly is excluded from MSBuild's incremental-build inputs. Without it, a change to the generator may not be detected and stale generated code may be benchmarked.
