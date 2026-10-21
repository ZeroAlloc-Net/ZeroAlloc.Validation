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

Measured 2026-10-21 against **FluentValidation 12.2.0**, the version the benchmark project
references, on an otherwise idle machine:

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Each table comes from one BenchmarkDotNet run in which ZeroAlloc.Validation and FluentValidation
were measured side by side. `Ratio` is each row's mean time relative to `FV_Valid` in that run, and
the speedups are FluentValidation's mean divided by ZeroAlloc's from the same run, with the lowest
and highest of the three launches in brackets. Every benchmark ran out of process in three
launches, pinned to two performance cores.

## Benchmark results — Flat model

Three directly validated properties: `[NotEmpty][MaxLength(50)]`, `[GreaterThan(0)]`, `[NotEmpty][EmailAddress]`.

| Method     | Ratio | Allocated | Alloc Ratio |
|----------- |------:|----------:|------------:|
| ZA_Valid   |  0.04 |         - |        0.00 |
| ZA_Invalid |  0.17 |     120 B |        0.18 |
| FV_Valid   |  1.00 |     664 B |        1.00 |
| FV_Invalid | 11.59 |    5408 B |        8.14 |

**Valid path:** ZeroAlloc is **22.7x faster** (21.2–23.6) and allocates **0 bytes** (vs 664 B).
**Invalid path:** ZeroAlloc is **68.8x faster** (68.5–69.4) and allocates **45.1x less** (120 B vs 5,408 B).

## Benchmark results — Nested model

One scalar property plus a required nested object (3 child properties).

| Method     | Ratio | Allocated | Alloc Ratio |
|----------- |------:|----------:|------------:|
| ZA_Valid   | 0.010 |         - |        0.00 |
| ZA_Invalid | 0.285 |     488 B |        0.33 |
| FV_Valid   | 1.000 |    1488 B |        1.00 |
| FV_Invalid | 5.941 |    6328 B |        4.25 |

**Valid path:** ZeroAlloc is **100.3x faster** (97.7–104.5) and allocates **0 bytes** (vs 1,488 B).
**Invalid path:** ZeroAlloc is **20.9x faster** (20.5–21.1) and allocates **13.0x less** (488 B vs 6,328 B).

## Benchmark results — Collection model

A cart with a string ID and a list of three line items.

| Method     | Ratio | Allocated | Alloc Ratio |
|----------- |------:|----------:|------------:|
| ZA_Valid   | 0.007 |         - |        0.00 |
| ZA_Invalid | 0.154 |     592 B |        0.17 |
| FV_Valid   | 1.000 |    3528 B |        1.00 |
| FV_Invalid | 3.925 |   11640 B |        3.30 |

**Valid path:** ZeroAlloc is **145.0x faster** (137.1–154.9) and allocates **0 bytes** (vs 3,528 B).
**Invalid path:** ZeroAlloc is **25.5x faster** (24.8–26.3) and allocates **19.7x less** (592 B vs 11,640 B).

## Benchmark results — Precision and enum rules

`[PrecisionScale(9, 2)]` on a decimal and `[IsInEnum]` on a concrete enum.

| Method     | Ratio | Allocated | Alloc Ratio |
|----------- |------:|----------:|------------:|
| ZA_Valid   | 0.016 |         - |        0.00 |
| ZA_Invalid | 0.023 |      88 B |        0.11 |
| FV_Valid   | 1.000 |     816 B |        1.00 |
| FV_Invalid | 3.006 |    6824 B |        8.36 |

**Valid path:** ZeroAlloc is **61.1x faster** (60.5–61.5) and allocates **0 bytes** (vs 816 B).
**Invalid path:** ZeroAlloc is **129.0x faster** (127.9–129.9) and allocates **77.5x less** (88 B vs 6,824 B).

## Summary

<!-- BENCH:START -->
_Last refreshed: 2026-10-21, against FluentValidation 12.2.0. Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published. Speedups are FluentValidation's mean divided by ZeroAlloc's, from the same BenchmarkDotNet run._

| Scenario | Speedup (valid) | Speedup (invalid) | ZA alloc (valid) | FV alloc (valid) | ZA alloc (invalid) | FV alloc (invalid) |
|---|---:|---:|---:|---:|---:|---:|
| Flat | **22.7×** | 68.8× | **0 B** | 664 B | 120 B | 5,408 B |
| Nested | **100.3×** | 20.9× | **0 B** | 1,488 B | 488 B | 6,328 B |
| Collection (3 items) | **145.0×** | 25.5× | **0 B** | 3,528 B | 592 B | 11,640 B |
| Precision and enum | **61.1×** | 129.0× | **0 B** | 816 B | 88 B | 6,824 B |

ZeroAlloc.Validation is **22.7–145.0× faster** than FluentValidation 12 on the valid path with **zero heap allocation**. On the invalid path it is 20.9–129.0× faster and allocates 13.0–77.5× less. FluentValidation allocates on every call, valid or not, for its per-call `List<ValidationFailure>` and validation context.
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
dotnet run -c Release -- --filter '*' --launchCount 3
```

Note: `--no-incremental` is required because the source generator's output assembly is excluded from MSBuild's incremental-build inputs. Without it, a change to the generator may not be detected and stale generated code may be benchmarked.
