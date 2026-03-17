# ZeroAlloc.Validation

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Validation)](https://www.nuget.org/packages/ZeroAlloc.Validation)
![Build](https://img.shields.io/github/actions/workflow/status/placeholder/ci.yml)
![License](https://img.shields.io/badge/license-MIT-blue)

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

See [Performance](docs/performance.md) for full benchmark results.

## Features

- Zero heap allocation on the valid path
- 25+ built-in validation attributes
- Nested object and collection validation
- ASP.NET Core auto-validation (HTTP 422 on failure)
- Per-rule severity (`Error`, `Warning`, `Info`)
- Conditional rules (`When` / `Unless` / `[SkipWhen]`)
- Short-circuit with `[StopOnFirstFailure]`
- Custom rules via `[Must]` predicates or `[CustomValidation]` methods
- Testing helpers via `ZeroAlloc.Validation.Testing`

## Documentation

- [Getting Started](docs/getting-started.md)
- [Attribute Reference](docs/attributes.md)
- [Nested Validation](docs/nested-validation.md)
- [Collection Validation](docs/collection-validation.md)
- [Custom Validation](docs/custom-validation.md)
- [Error Messages](docs/error-messages.md)
- [ASP.NET Core Integration](docs/aspnetcore.md)
- [Testing](docs/testing.md)
- [Performance](docs/performance.md)
- [Advanced Features](docs/advanced.md)
