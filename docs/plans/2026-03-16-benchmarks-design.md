# Benchmarks Design: ZeroAlloc.Validation vs FluentValidation

## Goal

Add a BenchmarkDotNet project that measures throughput and allocation counts for ZeroAlloc.Validation against FluentValidation across three model shapes (flat, nested, collection), on both the valid and invalid paths.

## Architecture

Standalone project under `benchmarks/` — not added to the solution file. Targets `net10.0` only. Uses BenchmarkDotNet with `[MemoryDiagnoser]` to capture bytes allocated per operation alongside throughput (ns/op).

The project references `ZeroAlloc.Validation` and `ZeroAlloc.Validation.Generator` as project references, and `FluentValidation` as a NuGet package.

The global `Directory.Build.props` analyzers (ZeroAlloc.Analyzers, Meziantou, etc.) would emit warnings/errors on FluentValidation code. A local `Directory.Build.props` in `benchmarks/` disables all inherited analyzers for that subtree.

## Benchmark Classes

Each class has four benchmark methods:

| Method | Description |
|--------|-------------|
| `ZA_Valid` | ZeroAlloc.Validation — valid instance (no failures) |
| `ZA_Invalid` | ZeroAlloc.Validation — invalid instance (failures present) |
| `FV_Valid` | FluentValidation — valid instance |
| `FV_Invalid` | FluentValidation — invalid instance |

### `FlatModelBenchmark`

Model: `BenchOrder` — flat, no nested objects.

```csharp
[Validate]
public class BenchOrder
{
    [NotEmpty]
    [MaxLength(50)]
    public string Reference { get; set; } = "";

    [GreaterThan(0)]
    public decimal Amount { get; set; }

    [NotEmpty]
    [EmailAddress]
    public string Email { get; set; } = "";
}
```

ZeroAlloc path: **flat path** — preallocated array, 0 heap allocations on valid path.

### `NestedModelBenchmark`

Models: `BenchOrderNested` + `BenchAddress`.

```csharp
[Validate]
public class BenchAddress
{
    [NotEmpty] public string Street { get; set; } = "";
    [NotEmpty] public string City { get; set; } = "";
    [NotEmpty] public string PostalCode { get; set; } = "";
}

[Validate]
public class BenchOrderNested
{
    [NotEmpty] public string Reference { get; set; } = "";
    [NotNull]  public BenchAddress? ShippingAddress { get; set; }
}
```

ZeroAlloc path: **mixed path** — `FailureBuffer` (ArrayPool-backed), 0 allocs valid / 1 alloc invalid.

### `CollectionModelBenchmark`

Models: `BenchCart` + `BenchLineItem` (list of 3 items).

```csharp
[Validate]
public class BenchLineItem
{
    [NotEmpty]   public string Sku { get; set; } = "";
    [GreaterThan(0)] public int Quantity { get; set; }
}

[Validate]
public class BenchCart
{
    [NotEmpty] public string CartId { get; set; } = "";
    public List<BenchLineItem> Items { get; set; } = [];
}
```

ZeroAlloc path: **mixed path** — `FailureBuffer`, fixed list of 3 items.

## FluentValidation Equivalents

Inline `AbstractValidator<T>` subclasses with equivalent rules using FluentValidation's default API:

- `NotEmpty()` → `NotEmpty()` / `NotNull()`
- `MaxLength(n)` → `MaximumLength(n)`
- `GreaterThan(n)` → `GreaterThan(n)`
- `EmailAddress` → `EmailAddress()`

Default cascade mode (all rules run) — equivalent to ZeroAlloc's default `Continue` mode.

## Project Structure

```
benchmarks/
  Directory.Build.props          ← disables inherited analyzers
  ZeroAlloc.Validation.Benchmarks/
    ZeroAlloc.Validation.Benchmarks.csproj
    Models/
      BenchOrder.cs
      BenchOrderNested.cs
      BenchAddress.cs
      BenchCart.cs
      BenchLineItem.cs
    Validators/
      FluentValidation/
        BenchOrderValidator.cs
        BenchOrderNestedValidator.cs
        BenchAddressValidator.cs
        BenchCartValidator.cs
        BenchLineItemValidator.cs
    Benchmarks/
      FlatModelBenchmark.cs
      NestedModelBenchmark.cs
      CollectionModelBenchmark.cs
    Program.cs
```

## Configuration

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class FlatModelBenchmark { ... }
```

`Program.cs` runs `BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args)`.

Results written to `BenchmarkDotNet.Artifacts/` (gitignored).

## Expected Results

| Benchmark | ZA Valid (alloc) | ZA Invalid (alloc) | FV Valid (alloc) | FV Invalid (alloc) |
|-----------|------------------|--------------------|------------------|--------------------|
| Flat | 0 B | ~N×40 B | >0 B | >0 B |
| Nested | 0 B | ~N×40 B | >0 B | >0 B |
| Collection | 0 B | ~N×40 B | >0 B | >0 B |

(Exact numbers determined at runtime.)

## Not In Scope

- Multi-TFM benchmarks
- `StopOnFirstFailure` benchmarks
- `[CustomValidation]` / `[SkipWhen]` benchmarks
- Publishing results to CI artifacts
