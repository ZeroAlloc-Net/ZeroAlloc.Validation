# ZeroAlloc.Validation Benchmarks

Compares **ZeroAlloc.Validation** against **FluentValidation** across three model shapes, the precision and enum rules, and both the valid and invalid execution paths.

## Environment

Measured 2026-10-15 against **FluentValidation 12.1.1**, the version the benchmark project
references, with BenchmarkDotNet's in-process toolchain (`--inProcess`):

```
BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
Intel Core Ultra 7 255H 3.69GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> **Timings are indicative** — measured on a developer laptop (Core Ultra 7 255H, hybrid P/E cores)
> under background load; allocations are exact, ratios are same-run. Each table comes from one
> BenchmarkDotNet run in which the ZeroAlloc.Validation and FluentValidation rows were measured
> side by side, so compare ratios rather than absolute nanoseconds.

The classes also carry `[SimpleJob]`, so the same command measures an out-of-process job as well.
The tables below show the in-process job. Allocations were identical in both jobs. The
out-of-process job measured the ZeroAlloc invalid paths 1.6 to 2.4 times faster, for example
18.975 ns against 45.212 ns for the flat model. Most other rows agreed within 10%; the widest gap
was the nested FV_Valid row, 285.874 ns out of process against 360.254 ns in process.

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

| Method     | Mean         | Error      | StdDev     | Ratio | Allocated | Alloc Ratio |
|----------- |-------------:|-----------:|-----------:|------:|----------:|------------:|
| ZA_Valid   |     4.198 ns |  0.0232 ns |  0.0206 ns |  0.04 |         - |        0.00 |
| ZA_Invalid |    45.212 ns |  0.4384 ns |  0.4101 ns |  0.39 |     120 B |        0.17 |
| FV_Valid   |   116.833 ns |  1.4367 ns |  1.1997 ns |  1.00 |     696 B |        1.00 |
| FV_Invalid | 1,232.737 ns | 14.6328 ns | 13.6875 ns | 10.55 |    5216 B |        7.49 |

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

| Method     | Mean         | Error      | StdDev      | Ratio | Allocated | Alloc Ratio |
|----------- |-------------:|-----------:|------------:|------:|----------:|------------:|
| ZA_Valid   |     2.704 ns |  0.0659 ns |   0.0584 ns | 0.008 |         - |        0.00 |
| ZA_Invalid |   145.443 ns |  2.5690 ns |   6.6771 ns | 0.411 |     488 B |        0.33 |
| FV_Valid   |   360.254 ns | 17.6492 ns |  52.0392 ns | 1.019 |    1488 B |        1.00 |
| FV_Invalid | 1,848.720 ns | 71.4433 ns | 202.6729 ns | 5.229 |    6032 B |        4.05 |

**Valid path:** ZeroAlloc is **133.2× faster** and allocates **0 bytes** (vs 1,488 B).
**Invalid path:** ZeroAlloc is **12.7× faster** and allocates **12.4× less** (488 B vs 6,032 B).

This class was run twice: in the first run the ZA_Invalid row's Error was 6% of its Mean while
another build loaded the machine. The table is the second run, where every row's Error is within
5% of its Mean.

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

| Method     | Mean         | Error      | StdDev     | Ratio | Allocated | Alloc Ratio |
|----------- |-------------:|-----------:|-----------:|------:|----------:|------------:|
| ZA_Valid   |     7.896 ns |  0.0493 ns |  0.0462 ns |  0.01 |         - |        0.00 |
| ZA_Invalid |   195.089 ns |  2.0402 ns |  1.9084 ns |  0.27 |     592 B |        0.17 |
| FV_Valid   |   723.962 ns | 13.8166 ns | 10.7871 ns |  1.00 |    3528 B |        1.00 |
| FV_Invalid | 2,735.001 ns | 14.6138 ns | 12.9548 ns |  3.78 |   11264 B |        3.19 |

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

| Method     | Mean        | Error     | StdDev     | Ratio | Allocated | Alloc Ratio |
|----------- |------------:|----------:|-----------:|------:|----------:|------------:|
| ZA_Valid   |    13.31 ns |  0.125 ns |   0.117 ns |  0.02 |         - |        0.00 |
| ZA_Invalid |    31.90 ns |  0.288 ns |   0.255 ns |  0.05 |      88 B |        0.11 |
| FV_Valid   |   625.32 ns | 14.737 ns |  41.565 ns |  1.00 |     816 B |        1.00 |
| FV_Invalid | 2,005.95 ns | 53.111 ns | 153.239 ns |  3.22 |    6593 B |        8.08 |

**Valid path:** ZeroAlloc is **47.0× faster** and allocates **0 bytes** (vs 816 B).
**Invalid path:** ZeroAlloc is **62.9× faster** and allocates **74.9× less** (88 B vs 6,593 B).

---

## Summary

Speedups are FluentValidation's mean divided by ZeroAlloc's, from the same run.

| Scenario   | Speedup (valid) | Speedup (invalid) | ZA alloc (valid) | FV alloc (valid) | ZA alloc (invalid) | FV alloc (invalid) | ZA valid | FV valid |
|------------|----------------:|------------------:|-----------------:|-----------------:|-------------------:|-------------------:|---------:|---------:|
| Flat       |  27.8× | 27.3× | 0 B |   696 B | 120 B |  5,216 B | 4.198 ns | 116.833 ns |
| Nested     | 133.2× | 12.7× | 0 B | 1,488 B | 488 B |  6,032 B | 2.704 ns | 360.254 ns |
| Collection |  91.7× | 14.0× | 0 B | 3,528 B | 592 B | 11,264 B | 7.896 ns | 723.962 ns |
| Precision and enum | 47.0× | 62.9× | 0 B | 816 B | 88 B | 6,593 B | 13.31 ns | 625.32 ns |

The valid path is the hot path in most applications. ZeroAlloc.Validation allocates nothing on the
valid path because the source generator emits a **lazy-allocation** pattern: the failure buffer is
only created on the first rule violation, and a valid result returns `Array.Empty<ValidationFailure>()`
— a static singleton.

On the invalid path ZeroAlloc.Validation still outperforms FluentValidation significantly both in
speed and allocations, because it avoids reflection, expression trees, and the boxing/collection
overhead that FluentValidation incurs when building its error list.

### What the invalid path allocates

A failing validation allocates its result array, one 32-byte `ValidationFailure` per failure plus a
24-byte array header, and nothing else for a flat model. A nested or collection validator adds the
child validator's own result array and one string per child failure for its dotted property path.
The invalid-path figures above add up exactly:

| Model | Failures | Allocated |
|---|---|---:|
| Flat | 3 | 120 B: one 3-failure result array |
| Precision and enum | 2 | 88 B: one 2-failure result array |
| Nested | 1 direct, 3 from `ShippingAddress` | 488 B: the child's 3-failure array, 120 B; the parent's 4-failure array, 152 B; the paths `ShippingAddress.Street`, `.City` and `.PostalCode`, 72, 64 and 80 B |
| Collection | 1 direct, 4 from 3 items | 592 B: the items' arrays, 88, 56 and 56 B; the parent's 5-failure array, 184 B; the paths `Items[0].Sku`, `Items[0].Quantity`, `Items[1].Quantity` and `Items[2].Sku`, 48, 56, 56 and 48 B |

The tables in [docs/performance.md](../../docs/performance.md#allocation-on-the-invalid-path) list
56 to 120 B because they cover flat models with one to three failures. The 304, 608 and 856 B these
benchmarks reported before were measured before the failure buffer moved to `ArrayPool`, when the
staging buffer itself was allocated on every failing call.

