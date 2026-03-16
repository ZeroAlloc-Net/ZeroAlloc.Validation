# Benchmark Project Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a BenchmarkDotNet project under `benchmarks/` that compares ZeroAlloc.Validation against FluentValidation across flat, nested, and collection model shapes — measuring throughput and allocation counts on both the valid and invalid paths.

**Architecture:** Standalone `benchmarks/` directory with its own `Directory.Build.props` that suppresses the root-level analyzers (which would error on FluentValidation code). The project references `ZeroAlloc.Validation` + its generator as project references and `FluentValidation` as a NuGet package. Each benchmark class has four methods: `ZA_Valid`, `ZA_Invalid`, `FV_Valid`, `FV_Invalid`.

**Tech Stack:** BenchmarkDotNet (latest stable), FluentValidation 11.11.0, net10.0, `[MemoryDiagnoser]`, `[SimpleJob]` (uses current runtime — no RuntimeMoniker needed).

---

### Task 1: Project scaffold

**Files:**
- Create: `benchmarks/Directory.Build.props`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/ZeroAlloc.Validation.Benchmarks.csproj`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Program.cs`

**Step 1: Create `benchmarks/Directory.Build.props`**

This file stops MSBuild from walking up to the root `Directory.Build.props`, preventing the strict analyzers from running on FluentValidation code. It manually re-declares the properties we want.

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <!-- TreatWarningsAsErrors intentionally omitted: BenchmarkDotNet generates some obsolete warnings -->
  </PropertyGroup>
</Project>
```

**Step 2: Create the `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
    <PackageReference Include="FluentValidation" Version="11.11.0" />
  </ItemGroup>

  <ItemGroup>
    <!-- Main library (attributes + runtime types) -->
    <ProjectReference Include="..\..\src\ZeroAlloc.Validation\ZeroAlloc.Validation.csproj" />
    <!-- Generator: must be referenced separately to activate source generation on this project -->
    <ProjectReference Include="..\..\src\ZeroAlloc.Validation.Generator\ZeroAlloc.Validation.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="true" />
  </ItemGroup>

</Project>
```

> **Note on BenchmarkDotNet version:** `0.14.0` may not have net10.0 support. If `dotnet build` fails with an unresolved `BenchmarkDotNet` reference, bump the version to the latest available: `dotnet add package BenchmarkDotNet` and accept whatever version NuGet resolves.

**Step 3: Create `Program.cs`**

```csharp
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```

**Step 4: Verify the project builds**

```bash
cd c:/Projects/Prive/ZValidation
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release
```

Expected: Build succeeded, 0 errors.

**Step 5: Commit**

```bash
git add benchmarks/
git commit -m "feat: add benchmark project scaffold"
```

---

### Task 2: Flat model benchmarks

Benchmarks the flat-path (no nested objects). ZeroAlloc emits a preallocated array — zero heap allocations on the valid path.

**Files:**
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Models/BenchOrder.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Validators/FVBenchOrderValidator.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Benchmarks/FlatModelBenchmark.cs`

**Step 1: Create `Models/BenchOrder.cs`**

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

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

**Step 2: Create `Validators/FVBenchOrderValidator.cs`**

```csharp
using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchOrderValidator : AbstractValidator<BenchOrder>
{
    public FVBenchOrderValidator()
    {
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
```

**Step 3: Create `Benchmarks/FlatModelBenchmark.cs`**

```csharp
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Validation.Benchmarks.Models;
using ZeroAlloc.Validation.Benchmarks.Validators;

namespace ZeroAlloc.Validation.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class FlatModelBenchmark
{
    private static readonly BenchOrderValidator _za = new();
    private static readonly FVBenchOrderValidator _fv = new();

    private static readonly BenchOrder _valid = new()
    {
        Reference = "ORD-2026-001",
        Amount    = 99.99m,
        Email     = "customer@example.com"
    };

    private static readonly BenchOrder _invalid = new()
    {
        Reference = "",
        Amount    = -1m,
        Email     = "not-an-email"
    };

    [Benchmark] public bool ZA_Valid()   => _za.Validate(_valid).IsValid;
    [Benchmark] public bool ZA_Invalid() => _za.Validate(_invalid).IsValid;
    [Benchmark] public bool FV_Valid()   => _fv.Validate(_valid).IsValid;
    [Benchmark] public bool FV_Invalid() => _fv.Validate(_invalid).IsValid;
}
```

> `BenchOrderValidator` is source-generated from `[Validate]` on `BenchOrder`. The generator produces `BenchOrderValidator : ValidatorFor<BenchOrder>` in the same namespace as the model.

**Step 4: Build and list benchmarks**

```bash
cd c:/Projects/Prive/ZValidation
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release
dotnet run --project benchmarks/ZeroAlloc.Validation.Benchmarks -c Release -- --list flat
```

Expected output includes:
```
ZeroAlloc.Validation.Benchmarks.Benchmarks.FlatModelBenchmark.ZA_Valid
ZeroAlloc.Validation.Benchmarks.Benchmarks.FlatModelBenchmark.ZA_Invalid
ZeroAlloc.Validation.Benchmarks.Benchmarks.FlatModelBenchmark.FV_Valid
ZeroAlloc.Validation.Benchmarks.Benchmarks.FlatModelBenchmark.FV_Invalid
```

**Step 5: Dry run to verify no runtime errors**

```bash
dotnet run --project benchmarks/ZeroAlloc.Validation.Benchmarks -c Release -- --filter *Flat* --job Dry --runtimes net10.0
```

Expected: All 4 benchmarks complete with no exceptions. (Dry job runs once, no warmup — fast.)

**Step 6: Commit**

```bash
git add benchmarks/
git commit -m "feat: add FlatModelBenchmark (ZeroAlloc vs FluentValidation)"
```

---

### Task 3: Nested model benchmarks

Benchmarks the mixed path with a nested object. ZeroAlloc uses `FailureBuffer` (ArrayPool-backed) — 0 allocs valid, 1 alloc invalid.

**Files:**
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Models/BenchAddress.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Models/BenchOrderNested.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Validators/FVBenchAddressValidator.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Validators/FVBenchOrderNestedValidator.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Benchmarks/NestedModelBenchmark.cs`

**Step 1: Create `Models/BenchAddress.cs`**

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

[Validate]
public class BenchAddress
{
    [NotEmpty] public string Street     { get; set; } = "";
    [NotEmpty] public string City       { get; set; } = "";
    [NotEmpty] public string PostalCode { get; set; } = "";
}
```

**Step 2: Create `Models/BenchOrderNested.cs`**

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

[Validate]
public class BenchOrderNested
{
    [NotEmpty] public string        Reference       { get; set; } = "";
    [NotNull]  public BenchAddress? ShippingAddress { get; set; }
}
```

**Step 3: Create `Validators/FVBenchAddressValidator.cs`**

```csharp
using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchAddressValidator : AbstractValidator<BenchAddress>
{
    public FVBenchAddressValidator()
    {
        RuleFor(x => x.Street).NotEmpty();
        RuleFor(x => x.City).NotEmpty();
        RuleFor(x => x.PostalCode).NotEmpty();
    }
}
```

**Step 4: Create `Validators/FVBenchOrderNestedValidator.cs`**

```csharp
using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchOrderNestedValidator : AbstractValidator<BenchOrderNested>
{
    public FVBenchOrderNestedValidator()
    {
        RuleFor(x => x.Reference).NotEmpty();
        RuleFor(x => x.ShippingAddress).NotNull()
            .SetValidator(new FVBenchAddressValidator()!);
    }
}
```

**Step 5: Create `Benchmarks/NestedModelBenchmark.cs`**

```csharp
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Validation.Benchmarks.Models;
using ZeroAlloc.Validation.Benchmarks.Validators;

namespace ZeroAlloc.Validation.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class NestedModelBenchmark
{
    private static readonly BenchOrderNestedValidator    _za = new();
    private static readonly FVBenchOrderNestedValidator  _fv = new();

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

    [Benchmark] public bool ZA_Valid()   => _za.Validate(_valid).IsValid;
    [Benchmark] public bool ZA_Invalid() => _za.Validate(_invalid).IsValid;
    [Benchmark] public bool FV_Valid()   => _fv.Validate(_valid).IsValid;
    [Benchmark] public bool FV_Invalid() => _fv.Validate(_invalid).IsValid;
}
```

**Step 6: Dry run**

```bash
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release
dotnet run --project benchmarks/ZeroAlloc.Validation.Benchmarks -c Release -- --filter *Nested* --job Dry
```

Expected: 4 benchmarks complete, no exceptions.

**Step 7: Commit**

```bash
git add benchmarks/
git commit -m "feat: add NestedModelBenchmark (ZeroAlloc vs FluentValidation)"
```

---

### Task 4: Collection model benchmarks

Benchmarks the mixed path with a collection. Uses a fixed list of 3 `BenchLineItem` instances.

**Files:**
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Models/BenchLineItem.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Models/BenchCart.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Validators/FVBenchLineItemValidator.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Validators/FVBenchCartValidator.cs`
- Create: `benchmarks/ZeroAlloc.Validation.Benchmarks/Benchmarks/CollectionModelBenchmark.cs`

**Step 1: Create `Models/BenchLineItem.cs`**

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

[Validate]
public class BenchLineItem
{
    [NotEmpty]      public string Sku      { get; set; } = "";
    [GreaterThan(0)] public int   Quantity { get; set; }
}
```

**Step 2: Create `Models/BenchCart.cs`**

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Benchmarks.Models;

[Validate]
public class BenchCart
{
    [NotEmpty] public string             CartId { get; set; } = "";
    public     List<BenchLineItem>       Items  { get; set; } = [];
}
```

**Step 3: Create `Validators/FVBenchLineItemValidator.cs`**

```csharp
using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchLineItemValidator : AbstractValidator<BenchLineItem>
{
    public FVBenchLineItemValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}
```

**Step 4: Create `Validators/FVBenchCartValidator.cs`**

```csharp
using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchCartValidator : AbstractValidator<BenchCart>
{
    public FVBenchCartValidator()
    {
        RuleFor(x => x.CartId).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(new FVBenchLineItemValidator());
    }
}
```

**Step 5: Create `Benchmarks/CollectionModelBenchmark.cs`**

```csharp
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Validation.Benchmarks.Models;
using ZeroAlloc.Validation.Benchmarks.Validators;

namespace ZeroAlloc.Validation.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class CollectionModelBenchmark
{
    private static readonly BenchCartValidator    _za = new();
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

    [Benchmark] public bool ZA_Valid()   => _za.Validate(_valid).IsValid;
    [Benchmark] public bool ZA_Invalid() => _za.Validate(_invalid).IsValid;
    [Benchmark] public bool FV_Valid()   => _fv.Validate(_valid).IsValid;
    [Benchmark] public bool FV_Invalid() => _fv.Validate(_invalid).IsValid;
}
```

**Step 6: Full dry run of all benchmarks**

```bash
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release
dotnet run --project benchmarks/ZeroAlloc.Validation.Benchmarks -c Release -- --filter * --job Dry
```

Expected: All 12 benchmarks listed and completed without exceptions:
```
FlatModelBenchmark.ZA_Valid / ZA_Invalid / FV_Valid / FV_Invalid
NestedModelBenchmark.ZA_Valid / ZA_Invalid / FV_Valid / FV_Invalid
CollectionModelBenchmark.ZA_Valid / ZA_Invalid / FV_Valid / FV_Invalid
```

**Step 7: Add `BenchmarkDotNet.Artifacts/` to `.gitignore`**

Open `.gitignore` (or create at root if absent). Add:

```
BenchmarkDotNet.Artifacts/
```

**Step 8: Commit**

```bash
git add benchmarks/ .gitignore
git commit -m "feat: add CollectionModelBenchmark and complete benchmark suite"
```
