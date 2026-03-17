---
id: advanced
title: Advanced Features
slug: /docs/advanced
description: Conditional validation with [SkipWhen], per-property short-circuiting with [StopOnFirstFailure], per-rule When/Unless guards, and Severity.
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
