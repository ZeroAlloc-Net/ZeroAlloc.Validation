---
id: inject
title: DI Registration (Inject)
slug: /docs/inject
description: Bulk-register all ZeroAlloc.Validation validators in one call with AddZeroAllocValidators().
sidebar_position: 8
---

## Installation

```bash
dotnet add package ZeroAlloc.Validation
dotnet add package ZeroAlloc.Validation.Inject
```

## Setup

```csharp
services.AddZeroAllocValidators();
```

That's all. Every class annotated with `[Validate]` in your project gets its generated validator registered as a `Singleton` in one call.

## What it emits

`ZeroAlloc.Validation.Inject` is a generator-only package — it contains no runtime code. At build time, the source generator scans for all `[Validate]` classes and emits an extension method in your assembly:

```csharp
// generated in your assembly
public static class ZeroAllocValidatorRegistrationExtensions
{
    public static IServiceCollection AddZeroAllocValidators(
        this IServiceCollection services)
    {
        services.TryAddSingleton<ValidatorFor<DatabaseOptions>, DatabaseOptionsValidator>();
        services.TryAddSingleton<ValidatorFor<SmtpOptions>, SmtpOptionsValidator>();
        // one line per [Validate] class
        return services;
    }
}
```

Validators are registered as `ValidatorFor<T>` — the abstract base type — so any consumer can resolve by the interface without knowing the concrete generated class.

## Idempotency

All registrations use `TryAddSingleton`. Calling `AddZeroAllocValidators()` multiple times, or alongside `AddZeroAllocAspNetCoreValidation()` or `.ValidateWithZeroAlloc()`, produces no duplicate registrations.

```csharp
// All three are safe to call together — no duplicates
services.AddZeroAllocValidators();
services.AddZeroAllocAspNetCoreValidation();
services.AddOptions<DatabaseOptions>().ValidateWithZeroAlloc();
```

## Validators without DI

Validators are always usable without DI — the generated classes are plain classes with a parameterless constructor:

```csharp
var validator = new DatabaseOptionsValidator();
var result = validator.Validate(options);
```

DI registration via `AddZeroAllocValidators()` is opt-in.
