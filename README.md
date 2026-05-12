# ZeroAlloc.Validation

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Validation.svg)](https://www.nuget.org/packages/ZeroAlloc.Validation)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Source-generated, attribute-based validation for .NET that allocates nothing on the valid path. The source generator emits a strongly-typed validator class at build time — no reflection at runtime. When all rules pass, the entire validation cycle produces zero heap allocations.

## Install

```bash
dotnet add package ZeroAlloc.Validation
```

## 30-Second Example

```csharp
using ZeroAlloc.Validation;

[Validate]
public class CreateOrderRequest
{
    [NotEmpty][MaxLength(50)] public string  Reference { get; set; } = "";
    [GreaterThan(0)]          public decimal Amount    { get; set; }
    [NotEmpty][EmailAddress]  public string  Email     { get; set; } = "";
}

// The source generator emits CreateOrderRequestValidator at build time
var request   = new CreateOrderRequest
{
    Reference = "ORD-2026-001",
    Amount    = 99.99m,
    Email     = "customer@example.com"
};
var validator = new CreateOrderRequestValidator();
var result    = validator.Validate(request);

if (!result.IsValid)
    foreach (ref readonly var f in result.Failures)
        Console.WriteLine($"{f.PropertyName}: {f.ErrorMessage}");
```

## Performance

| Scenario         | ZeroAlloc.Validation | FluentValidation | Speedup | Allocation (valid) |
|------------------|---------------------:|-----------------:|:-------:|:------------------:|
| Flat model       |              6.7 ns  |         327 ns   |  ~49×   |        0 B         |
| Nested model     |             10.1 ns  |         619 ns   |  ~61×   |        0 B         |
| Collection (3×)  |             14.3 ns  |        2,043 ns  | ~143×   |        0 B         |

See [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/performance.md) for full benchmark results.

## Packages

| Package | Purpose |
|---|---|
| `ZeroAlloc.Validation` | Core library — attributes, source generator, `ValidationResult` |
| `ZeroAlloc.Validation.AspNetCore` | Auto-validates request models; returns HTTP 422 on failure |
| `ZeroAlloc.Validation.Inject` | Emits `AddZeroAllocValidators()` — bulk DI registration in one call |
| `ZeroAlloc.Validation.Options` | Emits `ValidateWithZeroAlloc()` — plugs validators into `Microsoft.Extensions.Options` |
| `ZeroAlloc.Validation.Testing` | Fluent assertions for unit-testing validators |

## Features

- Zero heap allocation on the valid path
- 25+ built-in validation attributes
- Nested object and collection validation
- ASP.NET Core auto-validation (HTTP 422 on failure)
- Zero-friction DI registration (`AddZeroAllocValidators()`)
- Source-generated `Microsoft.Extensions.Options` integration (`ValidateWithZeroAlloc()`)
- Per-rule severity (`Error`, `Warning`, `Info`)
- Conditional rules (`When` / `Unless` / `[SkipWhen]`)
- Short-circuit with `[StopOnFirstFailure]`
- Custom rules via `[Must]` predicates or `[CustomValidation]` methods
- Testing helpers via `ZeroAlloc.Validation.Testing`

## Documentation

- [Getting Started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/getting-started.md)
- [Attribute Reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/attributes.md)
- [Nested Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/nested-validation.md)
- [Collection Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/collection-validation.md)
- [Custom Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/custom-validation.md)
- [Error Messages](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/error-messages.md)
- [ASP.NET Core Integration](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/aspnetcore.md)
- [DI Registration (Inject)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/inject.md)
- [Options Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/options.md)
- [Testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/testing.md)
- [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/performance.md)
- [Advanced Features](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/advanced.md)
