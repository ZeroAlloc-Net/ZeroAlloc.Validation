---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZV0011–ZV0018 Roslyn analyzer rules emitted by ZeroAlloc.Validation.Generator, with triggers, severities, and fix guidance.
sidebar_position: 11
---

# Compiler Diagnostics

ZeroAlloc.Validation.Generator emits the following Roslyn diagnostics at compile time.

| ID | Severity | Title |
|---|---|---|
| [ZV0011](#zv0011) | Warning | Redundant [ValidateWith] attribute |
| [ZV0012](#zv0012) | Error | Invalid [ValidateWith] validator type |
| [ZV0013](#zv0013) | Error | Invalid [CustomValidation] method signature |
| [ZV0014](#zv0014) | Warning | [Validate] on non-readonly struct |
| [ZV0015](#zv0015) | Error | Duplicate pipeline behavior Order |
| [ZV0016](#zv0016) | Warning | Multi-property value-object can't be auto-unwrapped |
| [ZV0017](#zv0017) | Warning | Validation rules depending on an inaccessible base member are ignored |
| [ZV0018](#zv0018) | Warning | Duplicate validation attribute |

---

## ZV0011

**Severity:** Warning

**Title:** Redundant [ValidateWith] attribute

**When fired:** `[ValidateWith]` is applied to a property whose type already carries `[Validate]`. The auto-generated validator is used by default — `[ValidateWith]` is only needed for types you do not control.

**Fix:** Remove `[ValidateWith]` from the property and rely on the auto-generated validator, or keep it only if you need to override the default with a custom implementation.

---

## ZV0012

**Severity:** Error

**Title:** Invalid [ValidateWith] validator type

**When fired:** The type argument passed to `[ValidateWith(typeof(T))]` does not implement `ValidatorFor<TProperty>` for the property type.

**Fix:** Replace the type argument with a class that extends `ValidatorFor<TProperty>`, where `TProperty` matches the type of the annotated property.

---

## ZV0013

**Severity:** Error

**Title:** Invalid [CustomValidation] method signature

**When fired:** A method decorated with `[CustomValidation]` has parameters, or does not return `IEnumerable<ValidationFailure>`.

**Fix:** Ensure the method has no parameters and returns `IEnumerable<ValidationFailure>`:

```csharp
[CustomValidation]
public IEnumerable<ValidationFailure> ValidateBusinessRules()
{
    // yield return failures as needed
}
```

---

## ZV0014

**Severity:** Warning

**Title:** `[Validate]` on non-readonly struct

**When fired:** You decorated a `struct` or `record struct` with `[Validate]`,
but the type is not declared `readonly`. A caller can mutate the instance
between the validator returning `IsValid == true` and the consumer reading
the value — making the validation result stale.

**Fix:** Declare the type as `readonly struct` or `readonly record struct`:

```csharp
[Validate]
public readonly record struct PlaceOrderCommand(
    [property: GreaterThan(0)] int CustomerId,
    [property: GreaterThan(0)] decimal Total);
```

**Suppressing:** If your call site cooperates with the hazard (e.g. you validate
inside the same method that constructs the struct and never mutate after),
suppress with `#pragma warning disable ZV0014` around the type declaration,
or add `<NoWarn>$(NoWarn);ZV0014</NoWarn>` in the consuming project.

---

## ZV0015

**Severity:** Error

**Title:** Duplicate pipeline behavior Order

**When fired:** Two `[PipelineBehavior]` classes targeting the same model have the same `Order` value. The execution order of the behavior chain would be ambiguous.

**Fix:** Assign a unique `Order` value to each behavior:

```csharp
[PipelineBehavior(Order = 0)]
public class LoggingBehavior : IPipelineBehavior { /* ... */ }

[PipelineBehavior(Order = 1)]   // was also 0 — now unique
public class AuditBehavior : IPipelineBehavior { /* ... */ }
```

---

## ZV0016

**Severity:** Warning

**Title:** Multi-property value-object can't be auto-unwrapped

**When fired:** A property carries a built-in operand validator (e.g. `[GreaterThan]`, `[NotEmpty]`) and its type is decorated with `[ZeroAlloc.ValueObjects.ValueObject]` but exposes more than one public instance property. Auto-unwrap only works for single-property wrappers — there is no single underlying value to compare against.

**Fix:** Either expose the validation through a single-property wrapper, or replace the built-in operand validator with `[Must]` / `[CustomValidation]` carrying a custom predicate that knows how to inspect the multi-property type:

```csharp
[ValueObject]
public readonly partial struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }
    public Money(decimal amount, string currency) { Amount = amount; Currency = currency; }
}

[Validate]
public partial class PriceCommand
{
    // [GreaterThan(0)] Money Total      // would emit ZV0016 — Money has two properties
    [Must(nameof(IsPositive))]
    public Money Total { get; set; }

    public bool IsPositive(Money m) => m.Amount > 0;
}
```

**Suppressing:** If your intent is to constrain a different property (e.g. only the `.Amount` component), refactor the model so the constrained surface is a single-property value-object. To silence the warning without restructuring, add `#pragma warning disable ZV0016` around the property declaration or `<NoWarn>$(NoWarn);ZV0016</NoWarn>` in the consuming project — but note that the underlying validator emission will still be incorrect for the multi-property case; a custom predicate is the recommended fix.

---

## ZV0017

**Severity:** Warning

**Title:** Validation rules depending on an inaccessible base member are ignored

**When fired:** A `[Validate]` type inherits from a base type that declares validation rules, but the member those rules depend on cannot be referenced from the generated validator. The generated validator is a separate class, so it can reach `public` members — and `internal` ones when the base type lives in the same assembly — but never `private` or `protected` ones. Two cases fire this:

- a base property carrying rule attributes is `protected` or `private` (or exposes no accessible getter);
- a rule on an otherwise-reachable property names a `When` / `Unless` method that is `protected` or `private` on a base type.

In both cases the rule is dropped rather than emitted as code that would not compile.

**Fix:** Widen the member to `public` (or `internal` within the same assembly), or move it onto the derived type:

```csharp
public abstract class AuditedBase
{
    [NotEmpty]
    protected string? ModifiedBy { get; init; }   // ZV0017 — rule silently dropped

    [NotEmpty(When = nameof(ShouldCheck))]
    public string? Reference { get; init; }       // ZV0017 — guard is unreachable

    protected bool ShouldCheck() => true;
}
```

```csharp
public abstract class AuditedBase
{
    [NotEmpty]
    public string? ModifiedBy { get; init; }      // reachable

    [NotEmpty(When = nameof(ShouldCheck))]
    public string? Reference { get; init; }

    public bool ShouldCheck() => true;            // reachable
}
```

**Suppressing:** If the member is deliberately hidden and you do not want it validated, set `[Validate(IncludeBaseProperties = false)]` on the derived type to opt out of base-type rules entirely, or add `<NoWarn>$(NoWarn);ZV0017</NoWarn>`. Note that suppressing leaves the rule unenforced.

---

## ZV0018

**Severity:** Warning

**Title:** Duplicate validation attribute

**When fired:** A property declares the same rule attribute more than once with identical arguments. The rule is evaluated twice and reports the same failure twice.

Rule attributes are `AllowMultiple` because repeating a check with *different* arguments is meaningful — two `[Must]` predicates, or a `[Matches]` for each of several patterns. Only an exact repeat is flagged:

```csharp
[Validate]
public class Request
{
    [NotEmpty]
    [NotEmpty]                       // ZV0018 — reports "must not be empty" twice
    public string? Tenant { get; init; }

    [MinLength(3)]
    [MinLength(5)]                   // fine — different bounds
    public string Region { get; init; } = "";

    [Must(nameof(IsShort))]
    [Must(nameof(IsLower))]          // fine — different predicates
    public string Code { get; init; } = "";
}
```

Named arguments are compared order-independently, so `[NotEmpty(ErrorCode = "A", Message = "x")]` and `[NotEmpty(Message = "x", ErrorCode = "A")]` count as duplicates.

**Fix:** Remove the repeated attribute. If both were meant to check different things, give them different arguments.

**Suppressing:** `#pragma warning disable ZV0018` around the property, or `<NoWarn>$(NoWarn);ZV0018</NoWarn>`. Note that the duplicate failure is still reported at runtime.
