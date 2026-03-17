# ZeroAlloc.Validation Benchmarks

Compares **ZeroAlloc.Validation** against **FluentValidation 11** across three model shapes and both the valid and invalid execution paths.

## Environment

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
.NET SDK 10.0.104
  [Host]     : .NET 10.0.4 (10.0.426.12010), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.4 (10.0.426.12010), X64 RyuJIT AVX2
```

## Running the benchmarks

```bash
# From repo root
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release --no-incremental
cd benchmarks/ZeroAlloc.Validation.Benchmarks
dotnet run -c Release -- --filter '*' --job Default
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

| Method     | Mean         | Error      | StdDev      | Ratio | Allocated | Alloc Ratio |
|----------- |-------------:|-----------:|------------:|------:|----------:|------------:|
| ZA_Valid   |     6.713 ns |  0.4350 ns |    1.255 ns |  0.02 |         - |        0.00 |
| ZA_Invalid |    44.012 ns |  2.7703 ns |    7.859 ns |  0.14 |     304 B |        0.46 |
| FV_Valid   |   327.269 ns | 10.4974 ns |   29.436 ns |  1.00 |     664 B |        1.00 |
| FV_Invalid | 2,462.893 ns | 75.0023 ns |  210.315 ns |  7.58 |    5408 B |        8.14 |

**Valid path:** ZeroAlloc is **~49× faster** and allocates **0 bytes** (vs 664 B).
**Invalid path:** ZeroAlloc is **~56× faster** and allocates **~18× less** (304 B vs 5,408 B).

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

| Method     | Mean        | Error     | StdDev     | Ratio | Allocated | Alloc Ratio |
|----------- |------------:|----------:|-----------:|------:|----------:|------------:|
| ZA_Valid   |    10.09 ns |  0.890 ns |   2.526 ns |  0.02 |         - |        0.00 |
| ZA_Invalid |    96.56 ns |  2.411 ns |   6.841 ns |  0.16 |     608 B |        0.41 |
| FV_Valid   |   619.14 ns | 12.334 ns |  32.707 ns |  1.00 |    1488 B |        1.00 |
| FV_Invalid | 2,974.10 ns | 99.618 ns | 280.974 ns |  4.82 |    6328 B |        4.25 |

**Valid path:** ZeroAlloc is **~61× faster** and allocates **0 bytes** (vs 1,488 B).
**Invalid path:** ZeroAlloc is **~31× faster** and allocates **~10× less** (608 B vs 6,328 B).

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

| Method     | Mean        | Error      | StdDev      | Ratio | Allocated | Alloc Ratio |
|----------- |------------:|-----------:|------------:|------:|----------:|------------:|
| ZA_Valid   |    14.30 ns |   0.377 ns |    1.050 ns | 0.007 |         - |        0.00 |
| ZA_Invalid |   178.54 ns |   6.361 ns |   18.044 ns | 0.089 |     856 B |        0.25 |
| FV_Valid   | 2,042.95 ns |  89.644 ns |  254.305 ns | 1.000 |    3456 B |        1.00 |
| FV_Invalid | 5,957.29 ns | 249.827 ns |  704.642 ns | 2.958 |   11568 B |        3.35 |

**Valid path:** ZeroAlloc is **~143× faster** and allocates **0 bytes** (vs 3,456 B).
**Invalid path:** ZeroAlloc is **~33× faster** and allocates **~14× less** (856 B vs 11,568 B).

---

## Summary

| Scenario  | ZA valid | FV valid | Speedup | ZA alloc (valid) | FV alloc (valid) |
|-----------|:--------:|:--------:|:-------:|:----------------:|:----------------:|
| Flat      |  6.7 ns  | 327 ns   |  ~49×   |       0 B        |      664 B       |
| Nested    | 10.1 ns  | 619 ns   |  ~61×   |       0 B        |    1,488 B       |
| Collection| 14.3 ns  | 2,043 ns |  ~143×  |       0 B        |    3,456 B       |

The valid path is the hot path in most applications. ZeroAlloc.Validation allocates nothing on the
valid path because the source generator emits a **lazy-allocation** pattern: the failure buffer is
only created on the first rule violation, and a valid result returns `Array.Empty<ValidationFailure>()`
— a static singleton.

On the invalid path ZeroAlloc.Validation still outperforms FluentValidation significantly both in
speed and allocations, because it avoids reflection, expression trees, and the boxing/collection
overhead that FluentValidation incurs when building its error list.
