---
id: options
title: Options Validation
slug: /docs/options
description: Plug compile-time validators into Microsoft.Extensions.Options with ValidateWithZeroAlloc().
sidebar_position: 9
---

## Installation

```bash
dotnet add package ZeroAlloc.Validation
dotnet add package ZeroAlloc.Validation.Options
```

## Setup

```csharp
builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateWithZeroAlloc()
    .ValidateOnStart();
```

`ValidateWithZeroAlloc()` is source-generated — a strongly-typed overload is emitted for each `[Validate]` class in your project. If a class does not have `[Validate]`, no overload is generated and the compiler reports an error at the call site.

## What it emits

For each `[Validate]` class, the generator emits an extension method on `OptionsBuilder<T>`:

```csharp
// generated in your assembly
public static OptionsBuilder<DatabaseOptions> ValidateWithZeroAlloc(
    this OptionsBuilder<DatabaseOptions> builder)
{
    var services = builder.Services;
    services.TryAddSingleton<ValidatorFor<DatabaseOptions>, DatabaseOptionsValidator>();
    services.TryAddSingleton<IValidateOptions<DatabaseOptions>,
        ZeroAllocOptionsValidator<DatabaseOptions>>();
    return builder;
}
```

The generated method registers:
1. The validator as `ValidatorFor<T>` (singleton) — so it can also be resolved by other consumers
2. `ZeroAllocOptionsValidator<T>` as `IValidateOptions<T>` (singleton) — the bridge into the options pipeline

## How validation works at runtime

`ZeroAllocOptionsValidator<T>` is a thin adapter:

```csharp
public sealed class ZeroAllocOptionsValidator<T> : IValidateOptions<T> where T : class
{
    private readonly ValidatorFor<T> _validator;

    public ZeroAllocOptionsValidator(ValidatorFor<T> validator) => _validator = validator;

    public ValidateOptionsResult Validate(string? name, T options)
    {
        if (options is null) return ValidateOptionsResult.Skip;

        var result = _validator.Validate(options);
        if (result.IsValid) return ValidateOptionsResult.Success;

        var failures = result.Failures;
        var errors = new string[failures.Length];
        for (int i = 0; i < failures.Length; i++)
            errors[i] = $"{failures[i].PropertyName}: {failures[i].ErrorMessage}";
        return ValidateOptionsResult.Fail(errors);
    }
}
```

No reflection — the compile-time `ValidatorFor<T>` does all the work.

## ValidateOnStart

Combine with `.ValidateOnStart()` to fail fast at application startup if configuration is invalid:

```csharp
builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateWithZeroAlloc()
    .ValidateOnStart();  // throws OptionsValidationException on startup if invalid
```

## Multiple options classes

Each `[Validate]` class gets its own overload. They compose independently:

```csharp
builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateWithZeroAlloc()
    .ValidateOnStart();

builder.Services.AddOptions<SmtpOptions>()
    .BindConfiguration("Smtp")
    .ValidateWithZeroAlloc()
    .ValidateOnStart();
```

## Idempotency

All registrations use `TryAddSingleton`. Calling `.ValidateWithZeroAlloc()` multiple times or alongside `AddZeroAllocValidators()` / `AddZeroAllocAspNetCoreValidation()` produces no duplicate registrations.
