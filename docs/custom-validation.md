---
id: custom-validation
title: Custom Validation
slug: /docs/custom-validation
description: Add inline predicates with [Must] and cross-property rules with [CustomValidation].
sidebar_position: 5
---

# Custom Validation

ZeroAlloc.Validation provides two customization mechanisms for rules that go beyond the built-in attributes.

| Mechanism | Placement | Scope |
|---|---|---|
| `[Must]` | Property | Single-property predicate |
| `[CustomValidation]` | Instance method | Full model access (cross-property) |

---

## Level 1: [Must] — Inline property predicate

Place `[Must(nameof(MethodName))]` on a property to call an instance method on the model as a predicate.

**Method signature requirement:** `bool MethodName(T value)` where `T` matches the property type.

The generator produces: `!instance.MethodName(instance.PropertyValue)`

**Default error message:** `{PropertyName} is invalid.`

### Example

```csharp
[Validate]
public class Product
{
    [NotEmpty]
    [Must(nameof(IsValidSku))]
    public string Sku { get; set; } = "";

    private bool IsValidSku(string value) =>
        value.StartsWith("SKU-") && value.Length >= 7;
}
```

The predicate is called on the instance being validated, so it can access any other instance member of the model (other properties, helper methods, etc.).

### Custom error message

Override the default message using the `Message` property inherited from `ValidationAttribute`:

```csharp
[Must(nameof(IsValidSku), Message = "SKU must start with 'SKU-' and be at least 7 characters.")]
public string Sku { get; set; } = "";
```

---

## Level 2: [CustomValidation] — Cross-property instance method

Place `[CustomValidation]` on an instance method of the model class (not a property) to run cross-property validation logic with full access to `this`.

**Method signature requirement:** no parameters, returns `IEnumerable<ValidationFailure>`.

The generator produces:

```csharp
foreach (var _cf in instance.MethodName()) _buf.Add(_cf);
```

`[CustomValidation]` methods run **after** all property-level rules have been evaluated. When `[Validate].StopOnFirstFailure = true`, custom methods are only reached if all property groups pass.

### Example

```csharp
[Validate]
public class PasswordChange
{
    [NotEmpty]
    public string CurrentPassword { get; set; } = "";

    [NotEmpty]
    [MinLength(8)]
    public string NewPassword { get; set; } = "";

    [NotEmpty]
    public string ConfirmPassword { get; set; } = "";

    [CustomValidation]
    public IEnumerable<ValidationFailure> ValidatePasswordMatch()
    {
        if (NewPassword != ConfirmPassword)
            yield return new ValidationFailure
            {
                PropertyName = nameof(ConfirmPassword),
                ErrorMessage = "Passwords do not match.",
                Severity     = Severity.Error
            };
    }
}
```

Your method constructs and yields `ValidationFailure` values directly, giving you full control over `PropertyName`, `ErrorMessage`, `ErrorCode`, and `Severity`.

### Multiple [CustomValidation] methods

Multiple `[CustomValidation]` methods are allowed on the same class. They run in declaration order, after all property rules.

### Important: [CustomValidation] does not extend ValidationAttribute

`[CustomValidation]` extends `System.Attribute` directly, **not** `ValidationAttribute`. It therefore does **not** support `Message`, `When`, `Unless`, `ErrorCode`, or `Severity` properties on the attribute itself. Your method is responsible for constructing the `ValidationFailure` values it yields.

---

## ZV0013 compiler diagnostic

If a method decorated with `[CustomValidation]` has the wrong signature — has parameters, or does not return `IEnumerable<ValidationFailure>` — the generator emits a **ZV0013** compile-time error:

> Method 'MethodName' decorated with [CustomValidation] must have no parameters and return IEnumerable\<ValidationFailure\>

This is caught at compile time, not at runtime.

---

## Choosing between [Must] and [CustomValidation]

| Use case | Recommendation |
|---|---|
| Single-property predicate (pure validation) | `[Must]` — concise, inline |
| Cross-property rules (e.g., confirm password) | `[CustomValidation]` — full access to model |
| Complex rule needing custom error code/severity | `[CustomValidation]` — construct the `ValidationFailure` directly |
| Simple format check | `[Must]` |
