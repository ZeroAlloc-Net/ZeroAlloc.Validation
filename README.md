# ZeroAlloc.Validation

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Validation.svg)](https://www.nuget.org/packages/ZeroAlloc.Validation)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Source-generated, attribute-based validation for .NET that allocates nothing on the valid path. The source generator emits a strongly-typed validator class at build time — no reflection at runtime. When all rules pass, the entire validation cycle produces zero heap allocations.

## Install

```bash
dotnet add package ZeroAlloc.Validation
```

`ZeroAlloc.Validation` bundles its own source generator — no other package is required.

> **Upgrading from an older version?** If your project also references
> `ZeroAlloc.Validation.Generator` directly, remove that `PackageReference` — the generator
> now ships inside `ZeroAlloc.Validation` itself, and keeping both loads it twice, which fails
> the build with `ZV9001`.

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

| Scenario         | Speedup vs FluentValidation 12 | Allocation (valid) |
|------------------|:------------------------------:|:------------------:|
| Flat model       |  27.8×  | 0 B vs 696 B   |
| Nested model     | 133.2×  | 0 B vs 1,488 B |
| Collection (3×)  |  91.7×  | 0 B vs 3,528 B |

Valid path, FluentValidation 12.1.1, measured 2026-10-15. Allocations are exact. Speed is shown as same-run ratios, because the benchmark host is a shared VM; absolute timings are not published. The speedup is FluentValidation's mean divided by ZeroAlloc's, from the same BenchmarkDotNet run. See [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/performance.md) for the invalid path and the full benchmark results.

## Packages

| Package | Purpose |
|---|---|
| `ZeroAlloc.Validation` | Core library — attributes, source generator, `ValidationResult` |
| `ZeroAlloc.Validation.AspNetCore` | Auto-validates request models; returns HTTP 422 on failure |
| `ZeroAlloc.Validation.Inject` | Emits `AddZeroAllocValidators()` — bulk DI registration in one call |
| `ZeroAlloc.Validation.Options` | Emits `ValidateWithZeroAlloc()` — plugs validators into `Microsoft.Extensions.Options` |
| `ZeroAlloc.Validation.Testing` | Fluent assertions for unit-testing validators |
| ~~`ZeroAlloc.Validation.Generator`~~ | Deprecated. Bundled into `ZeroAlloc.Validation` itself; do not reference directly |

## Features

- Zero heap allocation on the valid path
- 20 built-in rule attributes, plus `[Must]` and `[CustomValidation]` for your own
- Nested object and collection validation
- Inherited rules from base types (opt out with `[Validate(IncludeBaseProperties = false)]`)
- ASP.NET Core auto-validation (HTTP 422 on failure)
- Zero-friction DI registration (`AddZeroAllocValidators()`)
- Source-generated `Microsoft.Extensions.Options` integration (`ValidateWithZeroAlloc()`)
- Per-rule severity (`Error`, `Warning`, `Info`)
- Conditional rules (`When` / `Unless` / `[SkipWhen]`)
- Short-circuit with `[StopOnFirstFailure]`
- Custom rules via reusable `ValidationAttribute<T>` attributes, `[Must]` predicates or `[CustomValidation]` methods
- Asynchronous rules via `AsyncValidationAttribute<T>`, awaited by the generated `ValidateAsync`
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
- [Diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/diagnostics.md)
- [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/performance.md)
- [Advanced Features](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/advanced.md)
- [Migrating to v2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/migrating-to-v2.md)
