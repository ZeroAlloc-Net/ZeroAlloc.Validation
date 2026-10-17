# ZeroAlloc.Validation

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Validation.svg)](https://www.nuget.org/packages/ZeroAlloc.Validation)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Validation/graph/badge.svg)](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Validation)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Source-generated, attribute-based alternative to FluentValidation for .NET: no reflection, 0 bytes allocated when a model is valid, and NativeAOT-ready.

## Why ZeroAlloc.Validation

- **Nothing allocated on the valid path.** A passing validation allocates 0 B, asserted by allocation regression tests for flat, nested and collection models. FluentValidation 12 allocates 696–3,528 B for the same models.
- **27.8–133.2× faster than FluentValidation 12** on the valid path, measured side by side in the same BenchmarkDotNet run.
- **Works under NativeAOT and trimming.** The validator is plain generated C#, with no reflection or expression trees; CI publishes and runs a NativeAOT sample on every build.
- **Mistakes surface at compile time.** A numeric rule on a string, an `[IsInEnum]` on a non-enum or a
  `[CustomValidation]` method with the wrong signature fails the build, not a request.

## Install

```bash
dotnet add package ZeroAlloc.Validation
```

The source generator ships inside this package; nothing else is required.

## Quick start

```csharp
using System;
using ZeroAlloc.Validation;

var validator = new CreateOrderRequestValidator(); // generated at build time
var result = validator.Validate(new CreateOrderRequest { Reference = "", Amount = 0, Email = "nope" });

if (!result.IsValid)
    foreach (ref readonly var f in result.Failures)
        Console.WriteLine($"{f.PropertyName}: {f.ErrorMessage}");

[Validate]
public class CreateOrderRequest
{
    [NotEmpty, MaxLength(50)] public string  Reference { get; set; } = "";
    [GreaterThan(0)]          public decimal Amount    { get; set; }
    [NotEmpty, EmailAddress]  public string  Email     { get; set; } = "";
}
```

Mark a model with `[Validate]` and the generator writes `CreateOrderRequestValidator` for you. Running it prints:

```text
Reference: Reference must not be empty.
Amount: Amount must be greater than 0.
Email: Email must be a valid email address.
```

## What you get

- 20 built-in rules as attributes: emptiness, equality, ranges, lengths, regex, e-mail, enums, precision and scale.
- Your own rules through reusable attributes, inline `[Must]` predicates or `[CustomValidation]` methods.
- Asynchronous rules, awaited by the generated `ValidateAsync`.
- Nested objects and collections, with property paths such as `Items[0].Sku` in the failures.
- Rules inherited from base types, conditional rules with `When`, `Unless` and `[SkipWhen]`, and short-circuiting with `[StopOnFirstFailure]`.
- Per-rule severity, `Error`, `Warning` or `Info`, and custom error messages with placeholders.
- Pipeline behaviors that wrap validation calls, without a per-call allocation.

Optional packages:

- [`ZeroAlloc.Validation.AspNetCore`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/aspnetcore.md): validates request models automatically and returns HTTP 422 on failure.
- [`ZeroAlloc.Validation.Inject`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/inject.md): registers every validator in DI with one `AddZeroAllocValidators()` call.
- [`ZeroAlloc.Validation.Options`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/options.md): validates `Microsoft.Extensions.Options` at startup with `ValidateWithZeroAlloc()`.
- [`ZeroAlloc.Validation.Testing`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/testing.md): fluent assertions for unit-testing validators.

## Performance

| Scenario | Speedup vs FluentValidation 12 | Allocated (valid) |
|---|---:|---:|
| Flat model | 27.8× | 0 B vs 696 B |
| Nested model | 133.2× | 0 B vs 1,488 B |
| Collection, 3 items | 91.7× | 0 B vs 3,528 B |

Valid path, FluentValidation 12.1.1, measured 2026-10-15. Allocations are exact. Speed is shown as same-run ratios, because the benchmark host is a shared VM; absolute timings are not published. On the invalid path ZeroAlloc is 12.7–62.9× faster and allocates 12.4–74.9× less. See [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/performance.md) for the full results.

## Learn more

- **Documentation:** [validation.zeroalloc.net](https://validation.zeroalloc.net), including the [attribute reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/attributes.md) and the [diagnostics list](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/diagnostics.md).
- **Upgrading from v1:** [Migrating to v2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/migrating-to-v2.md) and the [changelog](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/CHANGELOG.md).

Related ZeroAlloc packages:

- [ZeroAlloc.Mediator](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator) ([docs](https://mediator.zeroalloc.net)): its `ZeroAlloc.Mediator.Validation` package runs these validators on every request before the handler.
- [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results) ([docs](https://results.zeroalloc.net)): a Mediator request that returns `Result<T, ValidationError>` gets a failed validation back as a failure result, not an exception.
- [ZeroAlloc.Pipeline](https://github.com/ZeroAlloc-Net/ZeroAlloc.Pipeline) ([docs](https://pipeline.zeroalloc.net)): the pipeline-behavior model this library's validator behaviors are built on.
- [ZeroAlloc.Authorization](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization) ([docs](https://authorization.zeroalloc.net)): plugs into the same Mediator pipeline and runs before validation, so a denied caller sees no validation errors.

Contributions are welcome: open an [issue](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues) or a pull request. Licensed under [MIT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/LICENSE).
