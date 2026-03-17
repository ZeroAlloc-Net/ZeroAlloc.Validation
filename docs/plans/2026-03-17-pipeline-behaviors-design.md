# Pipeline Behaviors Design

## Goal

Integrate `ZeroAlloc.Pipeline` into `ZeroAlloc.Validation` to support pre/post validation
behaviors, while also refactoring the generator internals to use `ZeroAlloc.Pipeline.Generators`
utilities. Zero regression on the existing zero-allocation path.

## Approach

**Approach B — Unified pipeline (pipeline IS the validator).**

The rule evaluation becomes the terminal step in a behavior chain. The generator detects at
compile time whether any behaviors apply to a given model:

- **No behaviors** → emits the current direct path (same as today, zero overhead)
- **Behaviors present** → emits a nested static lambda chain via `PipelineEmitter.EmitChain()`

## New Dependencies

| Project | New dependency |
|---|---|
| `ZeroAlloc.Validation` | `ZeroAlloc.Pipeline` (runtime contracts) |
| `ZeroAlloc.Validation.Generator` | `ZeroAlloc.Pipeline.Generators` (codegen utilities) |

## Architecture

Two layers managed by the generator at compile time:

**Layer 1 — Behavior chain (new):** Wraps the entire validation call. Behaviors run pre and
post. Emitted using `PipelineEmitter.EmitChain()`.

**Layer 2 — Rule chain (refactored):** The inner validation logic. Extracted as the terminal
step that `PipelineEmitter` wraps. Same logic as today, just better encapsulated.

```
ValidateAsync(instance, ct)
  → BehaviorN.Handle (outermost)
    → ...
      → Behavior1.Handle (innermost)
        → [rule evaluation terminal]
          → returns ValidationResult
```

## ValidatorFor<T> Changes

```csharp
public abstract partial class ValidatorFor<T>
{
    public abstract ValidationResult Validate(T instance);

    // Default: wraps sync Validate in a completed ValueTask.
    // Overridden by the generator when async behaviors are present.
    public virtual ValueTask<ValidationResult> ValidateAsync(
        T instance, CancellationToken ct = default)
        => ValueTask.FromResult(Validate(instance));
}
```

## Behavior Declaration

Uses `IPipelineBehavior` and `[PipelineBehavior]` from `ZeroAlloc.Pipeline` directly.
The `Handle` method must be `static`.

### Sync behavior

```csharp
[PipelineBehavior(Order = 0)]
public class ValidationLoggingBehavior : IPipelineBehavior
{
    public static ValidationResult Handle<TModel>(
        TModel instance,
        Func<TModel, ValidationResult> next)
    {
        // pre-validation logic
        var result = next(instance);
        // post-validation logic
        return result;
    }
}
```

### Async behavior

```csharp
[PipelineBehavior(Order = 0)]
public class CachingBehavior : IPipelineBehavior
{
    public static async ValueTask<ValidationResult> Handle<TModel>(
        TModel instance,
        CancellationToken ct,
        Func<TModel, CancellationToken, ValueTask<ValidationResult>> next)
    {
        // check cache
        return await next(instance, ct);
    }
}
```

### Scoping

| Attribute | Scope |
|---|---|
| `[PipelineBehavior(Order = 0)]` | Global — applies to all models |
| `[PipelineBehavior(Order = 1, AppliesTo = typeof(Order))]` | Per-model |

`Order` controls chain position. Lower = outermost. Multiple `AppliesTo` types are supported.

## Call Pattern

```csharp
// Sync — runs sync behaviors + rules
ValidationResult result = validator.Validate(order);

// Async — runs full pipeline (sync + async behaviors) + rules
ValidationResult result = await validator.ValidateAsync(order, ct);
```

**Rule:** Sync behaviors run in **both** `Validate()` and `ValidateAsync()`. Async behaviors
run **only** in `ValidateAsync()`.

The `ZValidationActionFilter` (ASP.NET Core) calls `ValidateAsync()` automatically, so all
behaviors (sync + async) run on every HTTP request for free.

## Generated Code Shape

### No behaviors (zero-change path)

```csharp
public sealed partial class OrderValidator : ValidatorFor<Order>
{
    public override ValidationResult Validate(Order instance)
    {
        // Same direct rule evaluation as today
        ValidationFailure[]? _buf = null;
        if (string.IsNullOrEmpty(instance.Reference))
            (_buf ??= new ValidationFailure[3])[0] = new(...);
        return new ValidationResult(_buf ?? []);
    }
    // ValidateAsync not overridden — inherits ValueTask.FromResult(Validate(instance))
}
```

### With behaviors

```csharp
public sealed partial class OrderValidator : ValidatorFor<Order>
{
    // Sync chain: behavior(s) → inner rules terminal
    public override ValidationResult Validate(Order instance)
        => ValidationLoggingBehavior.Handle(instance,
            static inst =>
            {
                ValidationFailure[]? _buf = null;
                // ... rules ...
                return new ValidationResult(_buf ?? []);
            });

    // Async chain: async behavior(s) → sync chain
    public override ValueTask<ValidationResult> ValidateAsync(
        Order instance, CancellationToken ct)
        => CachingBehavior.Handle(instance, ct,
            static (inst, token) =>
                ValueTask.FromResult(Validate(inst)));
}
```

## Generator Internals Refactor

| File | Change |
|---|---|
| `RuleEmitter.cs` | Trimmed to only emit the terminal rule body (innermost lambda). No behavior awareness. |
| `BehaviorDiscoverer.cs` | **New.** Thin wrapper around `PipelineBehaviorDiscoverer`. Returns global behaviors + per-model behaviors for a given type symbol. |
| `ValidatorGenerator.cs` | Orchestrates: calls `BehaviorDiscoverer`, then either the direct path (no behaviors) or `PipelineEmitter.EmitChain()` wrapping the rule terminal. |

## New Diagnostics

| ID | Severity | Trigger |
|---|---|---|
| ZV0014 | Warning | Async-only behavior exists but `Validate()` is the only call site — behaviors will not run |
| ZV0015 | Error | Two behaviors have the same `Order` value for the same model |

## Non-Goals

- No change to the attribute-based rule declaration API (`[NotEmpty]`, `[MaxLength]`, etc.)
- No change to `ValidationResult` or `ValidationFailure` structures
- No change to `ZeroAlloc.Validation.Testing` helpers
- Behaviors cannot add new `ValidationFailure` entries directly — they wrap the result, not inject into the rule buffer
