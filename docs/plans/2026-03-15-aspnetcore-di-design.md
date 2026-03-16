# ASP.NET Core Integration + DI Lifetime Forwarding Design

**Date:** 2026-03-15
**Status:** Approved

---

## Overview

Two related features delivered together:

1. **DI lifetime forwarding** — put `[Scoped]`/`[Transient]`/`[Singleton]` (ZeroAlloc.Inject) on a model alongside `[Validate]`; the core generator emits the same attribute on the generated validator so ZeroAlloc.Inject registers it automatically.

2. **ASP.NET Core auto-validation** — a new `ZeroAlloc.Validation.AspNetCore.Generator` scans all `[Validate]` models and emits a source-generated `ValidationActionFilter` with a type-switch dispatch (no reflection, fully AOT-safe) plus a `AddZValidationAutoValidation` extension method.

Both features are opt-in. The core library has no dependency on ZeroAlloc.Inject or ASP.NET Core.

---

## 1. DI Lifetime Forwarding

### Usage

```csharp
[Validate, Scoped]
public partial class Customer
{
    [NotEmpty] public string Name { get; set; } = "";
}
// Generator emits CustomerValidator decorated with [global::ZeroAlloc.Inject.Scoped]
// ZeroAlloc.Inject registers CustomerValidator automatically
```

### Detected FQNs (by string — no hard package dependency in generator)

| Attribute | FQN |
|---|---|
| `[Transient]` | `ZeroAlloc.Inject.TransientAttribute` |
| `[Scoped]` | `ZeroAlloc.Inject.ScopedAttribute` |
| `[Singleton]` | `ZeroAlloc.Inject.SingletonAttribute` |

### Generator change (`ValidatorGenerator.cs`)

When emitting the validator class header, check if the model class has one of the three lifetime attributes. If so, prepend it to the generated class:

```csharp
// Without lifetime:
public partial class CustomerValidator : global::ZeroAlloc.Validation.ValidatorFor<Customer>

// With [Scoped] on model:
[global::ZeroAlloc.Inject.Scoped]
public partial class CustomerValidator : global::ZeroAlloc.Validation.ValidatorFor<Customer>
```

The attribute is read from the model's `AttributeData` list by FQN — identical pattern to how validation attributes are detected.

### Key decisions

- No hard package reference to ZeroAlloc.Inject in `ZeroAlloc.Validation.Generator` — FQN string matching only
- Model without a lifetime attribute → no attribute on validator (unchanged behaviour)
- All three lifetimes supported; only one may be applied (Roslyn emits the first found)

---

## 2. New Project: `ZeroAlloc.Validation.AspNetCore.Generator`

### Project setup

```
src/
  ZeroAlloc.Validation.AspNetCore.Generator/
    ZeroAlloc.Validation.AspNetCore.Generator.csproj   ← netstandard2.0, IsRoslynComponent=true
    AspNetCoreFilterEmitter.cs                ← generator logic
```

`ZeroAlloc.Validation.AspNetCore.csproj` references it as an analyzer:

```xml
<ProjectReference Include="..\ZeroAlloc.Validation.AspNetCore.Generator\ZeroAlloc.Validation.AspNetCore.Generator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### What the generator emits

**File 1: `ZValidationActionFilter.g.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

internal sealed class ZValidationActionFilter : IActionFilter
{
    private readonly IServiceProvider _services;

    public ZValidationActionFilter(IServiceProvider services) => _services = services;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var arg in context.ActionArguments.Values)
        {
            var result = Dispatch(arg);
            if (result is null || result.Value.IsValid) continue;

            var pd = new ValidationProblemDetails();
            foreach (ref readonly var f in result.Value.Failures)
            {
                if (!pd.Errors.ContainsKey(f.PropertyName))
                    pd.Errors[f.PropertyName] = System.Array.Empty<string>();
                pd.Errors[f.PropertyName] = [.. pd.Errors[f.PropertyName], f.ErrorMessage];
            }
            context.Result = new UnprocessableEntityObjectResult(pd);
            return;
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private global::ZeroAlloc.Validation.ValidationResult? Dispatch(object? arg) => arg switch
    {
        global::MyApp.Customer c => _services.GetRequiredService<global::MyApp.CustomerValidator>().Validate(c),
        global::MyApp.Order o   => _services.GetRequiredService<global::MyApp.OrderValidator>().Validate(o),
        _                       => null
    };
}
```

**File 2: `ZValidationServiceCollectionExtensions.g.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ZValidationServiceCollectionExtensions
{
    public static IServiceCollection AddZValidationAutoValidation(this IServiceCollection services)
    {
        services.TryAddTransient<global::MyApp.CustomerValidator>();
        services.TryAddTransient<global::MyApp.OrderValidator>();
        services.TryAddTransient<ZValidationActionFilter>();
        services.Configure<MvcOptions>(o => o.Filters.Add<ZValidationActionFilter>());
        return services;
    }
}
```

### AOT safety

- `Dispatch` uses a C# `switch` expression with pattern matching — no `Type.GetType`, no `Activator.CreateInstance`, no reflection
- `GetRequiredService<T>()` with a concrete type is AOT-safe (no open generics)
- `ValidationProblemDetails` construction is plain object creation

### `TryAdd` semantics

Validators registered by `AddZValidationAutoValidation` use `TryAdd` — if the user already registered a validator with a different lifetime via ZeroAlloc.Inject, that registration wins.

---

## 3. Testing

### Core generator tests (existing `GeneratorRuleEmissionTests.cs`)

| Test | Assertion |
|---|---|
| `[Validate, Scoped]` model | Generated validator contains `[global::ZeroAlloc.Inject.Scoped]` |
| `[Validate, Transient]` model | Generated validator contains `[global::ZeroAlloc.Inject.Transient]` |
| `[Validate, Singleton]` model | Generated validator contains `[global::ZeroAlloc.Inject.Singleton]` |
| `[Validate]` without lifetime | Generated validator does NOT contain any lifetime attribute |

### ASP.NET Core generator tests (new test project or existing)

| Test | Assertion |
|---|---|
| Two `[Validate]` models | `Dispatch` switch contains both type arms |
| Two `[Validate]` models | Extension method contains `TryAddTransient` for both validators |
| Non-`[Validate]` type | Not present in switch or extension method |

### Integration tests (new `tests/ZeroAlloc.Validation.Tests.AspNetCore/`)

| Test | Assertion |
|---|---|
| `AddZValidationAutoValidation()` called | Filter registered, validators registered |
| Valid model POSTed | Action executes, no validation errors |
| Invalid model POSTed | 422 returned with `ValidationProblemDetails` matching property name + message |
| Unknown model type in action | Filter skips it, no error |

---

## Key Decisions

| Decision | Choice | Reason |
|---|---|---|
| Generator dependency on ZeroAlloc.Inject | FQN strings only, no package ref | Core generator stays dependency-free |
| Action filter validator resolution | `IServiceProvider.GetRequiredService<T>()` | Enables future validator dependencies; DI lifetime respected |
| Default validator lifetime in extension method | `TryAddTransient` | Safe default; ZeroAlloc.Inject registrations win via `TryAdd` ordering |
| HTTP status for validation failure | 422 Unprocessable Entity | RFC 9110 standard for semantic validation errors |
| Dispatch mechanism | Generated `switch` expression | AOT-safe, no reflection |
| `ZeroAlloc.Validation.AspNetCore.Generator` target | `netstandard2.0` | Consistent with `ZeroAlloc.Validation.Generator`; required for Roslyn analyzers |
