# ZeroAlloc.Validation Benchmarks

Compares **ZeroAlloc.Validation** against **FluentValidation** across three model shapes, the precision and enum rules, and both the valid and invalid execution paths.

## Environment

Measured 2026-10-15 against **FluentValidation 12.1.1**, the version the benchmark project
references, with BenchmarkDotNet's in-process toolchain (`--inProcess`), on a KVM virtual machine
on a shared host:

```
BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
Intel Core Ultra 7 255H 3.69GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios, because the benchmark host is a shared VM; absolute timings are not published.

Each table comes from one BenchmarkDotNet run in which ZeroAlloc.Validation and FluentValidation
were measured side by side. `Ratio` is each row's mean time relative to `FV_Valid` in that run, and
the speedups are FluentValidation's mean divided by ZeroAlloc's from the same run. The run used
the in-process toolchain, one launch per benchmark, without CPU pinning.

The classes also carry `[SimpleJob]`, so the same command measured an out-of-process job as well.
Its allocations were identical, but it measured the ZeroAlloc invalid paths faster relative to
FluentValidation: a `ZA_Invalid` ratio of 0.18 for the flat model, against 0.39 in process. The
in-process invalid-path speedups below are the more conservative of the two.

## Running the benchmarks

```bash
# From repo root
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release --no-incremental
cd benchmarks/ZeroAlloc.Validation.Benchmarks
dotnet run -c Release -- --filter '*' --job Default --inProcess
```

> `--no-incremental` is required because the source generator's output assembly is excluded from
> MSBuild's incremental-build inputs (`ReferenceOutputAssembly="false"`). Without it, a change to
> the generator is not detected and stale generated code may be benchmarked.

---

## Benchmark 1 — Flat model

Three directly validated properties: `[NotEmpty][MaxLength(50)]`, `[GreaterThan(0)]`,
`[NotEmpty][EmailAddress]`. No nesting or collections.

```csharp
[Validate]
public class BenchOrder
{
    [NotEmpty][MaxLength(50)] public string  Reference { get; set; } = "";
    [GreaterThan(0)]          public decimal Amount    { get; set; }
    [NotEmpty][EmailAddress]  public string  Email     { get; set; } = "";
}
```

| Method     | Ratio | Allocated | Alloc Ratio |
|----------- |------:|----------:|------------:|
| ZA_Valid   |  0.04 |         - |        0.00 |
| ZA_Invalid |  0.39 |     120 B |        0.17 |
| FV_Valid   |  1.00 |     696 B |        1.00 |
| FV_Invalid | 10.55 |    5216 B |        7.49 |

**Valid path:** ZeroAlloc is **27.8× faster** and allocates **0 bytes** (vs 696 B).
**Invalid path:** ZeroAlloc is **27.3× faster** and allocates **43.5× less** (120 B vs 5,216 B).

---

## Benchmark 2 — Nested model

One scalar property plus a required nested object, each with their own rules.

```csharp
[Validate]
public class BenchOrderNested
{
    [NotEmpty] public string        Reference       { get; set; } = "";
    [NotNull]  public BenchAddress? ShippingAddress { get; set; }
}

[Validate]
public class BenchAddress
{
    [NotEmpty] public string Street     { get; set; } = "";
    [NotEmpty] public string City       { get; set; } = "";
    [NotEmpty] public string PostalCode { get; set; } = "";
}
```

| Method     | Ratio | Allocated | Alloc Ratio |
|----------- |------:|----------:|------------:|
| ZA_Valid   | 0.008 |         - |        0.00 |
| ZA_Invalid | 0.411 |     488 B |        0.33 |
| FV_Valid   | 1.019 |    1488 B |        1.00 |
| FV_Invalid | 5.229 |    6032 B |        4.05 |

**Valid path:** ZeroAlloc is **133.2× faster** and allocates **0 bytes** (vs 1,488 B).
**Invalid path:** ZeroAlloc is **12.7× faster** and allocates **12.4× less** (488 B vs 6,032 B).

This class was run twice: in the first run the ZA_Invalid row's Error was 6% of its Mean while
another build loaded the machine. The table is the second run.

---

## Benchmark 3 — Collection model

A cart with a string ID and a list of three line items, each validated individually.

```csharp
[Validate]
public class BenchCart
{
    [NotEmpty] public string              CartId { get; set; } = "";
               public List<BenchLineItem> Items  { get; set; } = [];
}

[Validate]
public class BenchLineItem
{
    [NotEmpty]     public string Sku      { get; set; } = "";
    [GreaterThan(0)] public int  Quantity { get; set; }
}
```

_(3 items in both the valid and invalid fixture)_

| Method     | Ratio | Allocated | Alloc Ratio |
|----------- |------:|----------:|------------:|
| ZA_Valid   |  0.01 |         - |        0.00 |
| ZA_Invalid |  0.27 |     592 B |        0.17 |
| FV_Valid   |  1.00 |    3528 B |        1.00 |
| FV_Invalid |  3.78 |   11264 B |        3.19 |

**Valid path:** ZeroAlloc is **91.7× faster** and allocates **0 bytes** (vs 3,528 B).
**Invalid path:** ZeroAlloc is **14.0× faster** and allocates **19.0× less** (592 B vs 11,264 B).

---

## Benchmark 4 — Precision and enum rules

`[PrecisionScale]` on a decimal and `[IsInEnum]` on a concrete enum, both of which allocated on the
valid path before issue #316.

```csharp
[Validate]
public class BenchPayment
{
    [PrecisionScale(9, 2)] public decimal            Amount { get; set; }
    [IsInEnum]             public BenchPaymentStatus Status { get; set; }
}
```

| Method     | Ratio | Allocated | Alloc Ratio |
|----------- |------:|----------:|------------:|
| ZA_Valid   |  0.02 |         - |        0.00 |
| ZA_Invalid |  0.05 |      88 B |        0.11 |
| FV_Valid   |  1.00 |     816 B |        1.00 |
| FV_Invalid |  3.22 |    6593 B |        8.08 |

**Valid path:** ZeroAlloc is **47.0× faster** and allocates **0 bytes** (vs 816 B).
**Invalid path:** ZeroAlloc is **62.9× faster** and allocates **74.9× less** (88 B vs 6,593 B).

---

## Summary

Speedups are FluentValidation's mean divided by ZeroAlloc's, from the same run.

| Scenario | Speedup (valid) | Speedup (invalid) | ZA alloc (valid) | FV alloc (valid) | ZA alloc (invalid) | FV alloc (invalid) |
|---|---:|---:|---:|---:|---:|---:|
| Flat | **27.8×** | 27.3× | **0 B** | 696 B | 120 B | 5,216 B |
| Nested | **133.2×** | 12.7× | **0 B** | 1,488 B | 488 B | 6,032 B |
| Collection (3 items) | **91.7×** | 14.0× | **0 B** | 3,528 B | 592 B | 11,264 B |
| Precision and enum | **47.0×** | 62.9× | **0 B** | 816 B | 88 B | 6,593 B |
