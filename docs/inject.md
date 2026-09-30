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

`ZeroAlloc.Validation` bundles its own source generator, so these two packages are all that's
required. **Upgrading?** Remove any direct `ZeroAlloc.Validation.Generator` reference — keeping
it alongside `ZeroAlloc.Validation` loads the generator twice and fails the build with `ZV9001`.

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

## Composed validators

A validator for a model with a nested or collection `[Validate]` property takes the nested validator in its constructor as `ValidatorFor<TNested>` — the same service type the nested validator is registered under — so the container builds it from `AddZeroAllocValidators()` alone. The registration also covers every validator such a constructor needs that the `[Validate]` scan of your assembly would not find on its own:

```csharp
// for [Validate] class Order { Address Shipping; List<Line> Lines; [ValidateWith(typeof(MoneyChecker))] Money Total; }
services.TryAddSingleton<ValidatorFor<Order>, OrderValidator>();
services.TryAddSingleton<ValidatorFor<Address>, AddressValidator>();   // also when Address is in a referenced assembly
services.TryAddSingleton<ValidatorFor<Line>, LineValidator>();
services.TryAddSingleton<MoneyChecker>();                              // a [ValidateWith] validator, by its own type
```

- A nested model from a referenced assembly is registered when its generated validator is accessible from your assembly. An internal one is left to that assembly's own `AddZeroAllocValidators()`.
- A `[ValidateWith]` validator is registered by its own type, because that is what the constructor takes, unless it is abstract. It is registered as a **singleton**, like every validator here, so its own constructor dependencies come from the root container: it must not depend on scoped services, which the container would reject when scope validation is on and would otherwise capture for the life of the application. Register it yourself with the lifetime it needs before calling `AddZeroAllocValidators()` if that does not fit, bearing in mind that the composed validator holding it is a singleton too.
- `[ValidateWith(typeof(AddressValidator))]` naming the model's own generated validator, which ZV0011 reports as redundant, takes the ordinary `ValidatorFor<Address>` path.
- Only the properties the generated validator walks count: a base-class property left out by `[Validate(IncludeBaseProperties = false)]`, or one the validator cannot read, registers nothing.
- Registering your own `ValidatorFor<Address>` **before** calling `AddZeroAllocValidators()` replaces the nested validator every composed validator receives, because each registration is a `TryAdd`.

Before 2.0 the constructor took the nested validator's concrete type, which `AddZeroAllocValidators()` did not register, so resolving a composed validator threw. See [Migrating to v2](./migrating-to-v2.md).

## Generic models

A [generic model](getting-started.md#generic-models) is never registered as declared: nothing
closed can be registered for `Page<TItem>`, and an open-generic registration of
`ValidatorFor<>` to `PageValidator<>` cannot work, since the container maps the service's type
arguments to the implementation's by position. It would also make every other
`ValidatorFor<X>` lookup throw instead of returning null. Instead, each closing that a
registered validator takes is registered closed, with every closing its own validator takes:

```csharp
// for [Validate] class OrderPage { Page<Order> Page; }, with Page<TItem> holding List<Line<TItem>>
services.TryAddSingleton<ValidatorFor<OrderPage>, OrderPageValidator>();
services.TryAddSingleton<ValidatorFor<Page<Order>>, PageValidator<Order>>();
services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelValidator, ValidatorFor<Page<Order>>>(
    static sp => sp.GetRequiredService<ValidatorFor<Page<Order>>>()));
services.TryAddSingleton<ValidatorFor<Line<Order>>, LineValidator<Order>>();
services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelValidator, ValidatorFor<Line<Order>>>(
    static sp => sp.GetRequiredService<ValidatorFor<Line<Order>>>()));
```

- A closing is registered once, however many properties hold it, and a closing of a generic
  model from a referenced assembly is registered the same way.
- Each closing is also listed as an `IModelValidator`, the non-generic view every
  `ValidatorFor<T>` implements: its `ModelType` and a `ValidateAsync(object, CancellationToken)`.
  The entry resolves the `ValidatorFor` registration, so a registration you made first is the
  one listed, and `TryAddEnumerable` lists each closing once. A non-generic model gets no entry.
- A closing nothing registers, such as a `Page<Customer>` used only as a root, is not
  resolved: `GetService<ValidatorFor<Page<Customer>>>()` returns null, which ZeroAlloc.Mediator
  reads as "no validation". Construct it yourself, `new PageValidator<Customer>(...)`, or
  register it: `services.TryAddSingleton<ValidatorFor<Page<Customer>>, PageValidator<Customer>>()`.
- A `[Transient]`, `[Scoped]` or `[Singleton]` attribute on a generic model is copied onto its
  validator, and ZeroAlloc.Inject then registers the open `PageValidator<>` as itself. Nothing
  generated resolves a validator by its own type, and under NativeAOT an open-generic
  resolution of a value-type closing fails, so resolve `ValidatorFor<Page<int>>`, never
  `PageValidator<int>`.

## Idempotency

All registrations use `TryAddSingleton`. Calling `AddZeroAllocValidators()` multiple times, or alongside `AddZeroAllocAspNetCoreValidation()` or `.ValidateWithZeroAlloc()`, produces no duplicate registrations.

```csharp
// All three are safe to call together — no duplicates
services.AddZeroAllocValidators();
services.AddZeroAllocAspNetCoreValidation();
services.AddOptions<DatabaseOptions>().ValidateWithZeroAlloc();
```

## Validators without DI

Validators are always usable without DI. A validator for a flat model has a parameterless constructor; one that composes others takes each nested validator, and the generated one converts to the `ValidatorFor<T>` parameter:

```csharp
var validator = new DatabaseOptionsValidator();
var result = validator.Validate(options);

var orderValidator = new OrderValidator(new AddressValidator(), new LineValidator(), new MoneyChecker());
```

DI registration via `AddZeroAllocValidators()` is opt-in.
