---
id: advanced
title: Advanced Features
slug: /docs/advanced
description: Conditional validation with [SkipWhen], per-property short-circuiting with [StopOnFirstFailure], inherited rules from base types, per-rule When/Unless guards, and Severity.
sidebar_position: 10
---

# Advanced Features

## [SkipWhen] — Skip the entire model's validation

`[SkipWhen(nameof(MethodName))]` goes on the **class** (not a property). When the named method returns `true`, the entire `Validate()` call returns an empty (valid) `ValidationResult` immediately — no rules are checked.

- `[AttributeUsage(AttributeTargets.Class)]`
- Method: instance method, no parameters, returns `bool`
- If method returns `true` → skip all validation, return valid result
- If method returns `false` → proceed with normal validation

**Example use case:** Skip validation on draft orders that haven't been submitted yet.

```csharp
[Validate]
[SkipWhen(nameof(ShouldSkipValidation))]
public class Order
{
    public bool IsDraft { get; set; }

    [NotEmpty]
    public string Reference { get; set; } = "";

    [GreaterThan(0)]
    public decimal Amount { get; set; }

    private bool ShouldSkipValidation() => IsDraft;
}
```

When `IsDraft` is `true`, `validator.Validate(order)` returns `IsValid = true` with zero failures.

---

## [StopOnFirstFailure] — Per-property rule short-circuiting

Place it on a property to stop at that property's first failing rule, or on the class to apply the same to every property it validates.

`[StopOnFirstFailure]` goes on a **property**. When applied, the validator stops checking subsequent rules on that property after the first failing rule. Rules for other properties are still evaluated.

- `[AttributeUsage(AttributeTargets.Property)]`
- Stops only rules on the **same property** after the first failure — does NOT affect other properties

**Example:** When `NewPassword` is empty, skip the `[MinLength(8)]` and `[Matches]` checks — the error "NewPassword must not be empty" is sufficient.

```csharp
[Validate]
public class PasswordChange
{
    [NotEmpty]
    [MinLength(8)]
    [Matches(@"[A-Z]", Message = "Password must contain at least one uppercase letter.")]
    [StopOnFirstFailure]
    public string NewPassword { get; set; } = "";

    [NotEmpty]
    public string ConfirmPassword { get; set; } = "";
}
```

Without `[StopOnFirstFailure]`, an empty `NewPassword` produces three failures (`NotEmpty`, `MinLength`, `Matches`). With it, only the `NotEmpty` failure is reported.

---

## [Validate(StopOnFirstFailure = true)] — Model-level short-circuit

A named property on `[Validate]` itself: `[Validate(StopOnFirstFailure = true)]` stops after the **first failing property** (not the first failing rule within a property). Once any property produces at least one failure, all subsequent properties are skipped.

```csharp
[Validate(StopOnFirstFailure = true)]
public class CreateOrderRequest
{
    [NotEmpty]
    public string Reference { get; set; } = "";  // if this fails, Amount and Email are skipped

    [GreaterThan(0)]
    public decimal Amount { get; set; }

    [NotEmpty][EmailAddress]
    public string Email { get; set; } = "";
}
```

**When to use:** Useful when later rules depend on earlier fields being valid, or to return a single actionable error at a time (like a wizard UI that validates one step before moving to the next).

**It stops at the first failing property, not the first failing rule.** A property carrying several rules still reports every rule it violates before validation stops. To get literally one failure back, combine it with `[StopOnFirstFailure]` on the class, which applies the per-property cascade to every property at once:

```csharp
[Validate(StopOnFirstFailure = true)]
[StopOnFirstFailure]
public class TestRequest
{
    [NotEmpty][MinLength(3)] public string? Tenant { get; init; }
    [NotEmpty][MinLength(3)] public string? User { get; init; }
}
```

```csharp
// Tenant = "", User = ""
// Without the class-level attribute: 2 failures, both on Tenant.
// With it: 1 failure, Tenant must not be empty.
```

The class-level form is read from the type being validated, so it is not inherited from a base type — the same rule `[Validate(StopOnFirstFailure = true)]` already follows. It does govern rules inherited *into* that type.

**On the invalid path this is also the cheapest mode.** When a property can only ever produce one failure — it carries a single rule, or it carries `[StopOnFirstFailure]` so only the first matching rule fires — the generator returns that failure's array directly instead of filling a scratch buffer and copying out of it. Where every property in the model is like that, no buffer is emitted at all:

```csharp
[Validate(StopOnFirstFailure = true)]
public sealed class Request
{
    [NotEmpty]
    public string? PlayerId { get; init; }
}
```

```csharp
// generated
if (string.IsNullOrEmpty(instance.PlayerId))
{
    return new ValidationResult(new ValidationFailure[]
    {
        new ValidationFailure { PropertyName = "PlayerId", ErrorMessage = "PlayerId must not be empty." }
    });
}

return new ValidationResult(Array.Empty<ValidationFailure>());
```

A property with several rules and no `[StopOnFirstFailure]` can still report more than one failure, so it keeps using the buffer — the two shapes mix freely within one model. The valid path allocates nothing either way.

---

## Inheritance — Rules declared on base types

A generated validator enforces the rules declared on the model **and on every type it inherits from**. Inherited rules are checked first, base-most type before derived, so failures come back in the order the properties are declared down the hierarchy.

```csharp
[Validate]
public class BaseModel
{
    [NotEmpty]
    public string? Name { get; init; }
}

[Validate]
public class DerivedModel : BaseModel
{
    [GreaterThan(0)]
    public int Quantity { get; init; }
}
```

```csharp
var result = new DerivedModelValidator().Validate(
    new DerivedModel { Name = null, Quantity = 0 });

// Two failures, in hierarchy order: Name (from BaseModel), then Quantity.
```

This covers rule attributes, `[CustomValidation]` methods, nested `[Validate]` properties, and collection properties alike — wherever in the chain they are declared.

**The base type does not need `[Validate]`.** Rule attributes live on the properties; `[Validate]` only controls whether a validator is generated *for that type*. An abstract or shared base can carry rules without a validator of its own:

```csharp
public abstract class AuditedBase          // no [Validate] — no AuditedBaseValidator generated
{
    [NotEmpty]
    public string? ModifiedBy { get; init; }
}

[Validate]
public class Order : AuditedBase
{
    [GreaterThan(0)]
    public decimal Total { get; init; }
}

// OrderValidator enforces both ModifiedBy and Total.
```

**Hidden and overridden properties.** When a derived type redeclares a property with `new` or `override`, the most-derived declaration wins and its attributes are the ones applied — the base declaration's rules are not also run.

**Accessibility.** The generated validator is a separate class, so it can only reach `public` base members (and `internal` ones declared in the same assembly). Rules on a `protected` or `private` base member — or guarded by a `When` / `Unless` method that is `protected` or `private` on a base type — cannot be enforced, and are reported at compile time as [ZV0017](./diagnostics.md#zv0017) rather than silently dropped.

**Opting out.** Set `IncludeBaseProperties = false` to validate only the members declared directly on the type:

```csharp
[Validate(IncludeBaseProperties = false)]
public class DerivedModel : BaseModel
{
    [GreaterThan(0)]
    public int Quantity { get; init; }   // Name is not validated
}
```

`[SkipWhen]` and `[Validate(StopOnFirstFailure = true)]` are read from the type being validated only — they are not inherited from a base type's own `[Validate]`.

---

## When and Unless — Per-rule conditional guards

`ValidationAttribute` exposes `When` and `Unless` named properties. Both take a method name string (an instance method on the model with no parameters, returning `bool`).

- `When = nameof(Method)` — only validate this rule **if** `instance.Method()` returns `true`
- `Unless = nameof(Method)` — skip this rule **if** `instance.Method()` returns `true`

```csharp
[Validate]
public class Shipment
{
    public bool IsInternational { get; set; }

    [NotEmpty(When = nameof(IsInternational))]
    public string? CustomsCode { get; set; }

    [MaxLength(10, Unless = nameof(IsInternational))]
    public string? PostalCode { get; set; }
}
```

The method referenced by `When`/`Unless` must be a no-parameter instance method returning `bool`. Tip: wrap boolean properties in a method:

```csharp
private bool IsInternational() => IsInternational;
```

`When` and `Unless` work on all attributes that inherit from `ValidationAttribute` (i.e., all built-in rule attributes). They are NOT available on `[CustomValidation]` (which inherits from `System.Attribute` directly).

---

## Severity — Warnings and informational failures

Any rule attribute can set `Severity` to `Warning` or `Info` to classify the failure without preventing `IsValid` from being `false`.

```csharp
[MaxLength(500, Severity = Severity.Warning, Message = "Bio is long — consider trimming.")]
public string Bio { get; set; } = "";
```

All failures (regardless of severity) are included in `result.Failures`. `result.IsValid` is `false` whenever any failures exist.

To treat warnings as soft: filter `result.Failures` by severity in the calling code:

```csharp
bool hardFail = false;
foreach (ref readonly var f in result.Failures)
{
    if (f.Severity == Severity.Error)
    {
        hardFail = true;
        break;
    }
}
```
