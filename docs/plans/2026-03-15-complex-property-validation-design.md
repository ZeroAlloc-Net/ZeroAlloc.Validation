# Complex Property Validation Design

**Date:** 2026-03-15
**Status:** Approved

---

## Overview

When a property's type is itself decorated with `[Validate]`, the generator automatically includes nested validation in the parent validator. No new attributes required — convention-driven, consistent with the rest of the library.

---

## 1. Declaration

No extra attributes. The generator detects nested `[Validate]` types automatically:

```csharp
[Validate]
public class Address
{
    [NotEmpty]
    public string Street { get; set; } = "";

    [NotEmpty]
    public string City { get; set; } = "";
}

[Validate]
public class Customer
{
    [NotEmpty]
    public string Name { get; set; } = "";

    // Address has [Validate] → automatically validated, dot-prefixed failures
    public Address Address { get; set; } = new();

    // string has no [Validate] → not recursed into
    public string Notes { get; set; } = "";
}
```

**Null handling:** If the nested property is `null` at runtime, nested validation is silently skipped. Use `[NotNull]` explicitly on the property if null should produce a failure.

---

## 2. Generated Code Shape

The generator switches from a fixed array to `List<ValidationFailure>` only when the model has at least one nested `[Validate]` property. Flat models are unaffected.

```csharp
// Generated for Customer (has nested Address → uses List)
public sealed partial class CustomerValidator : ValidatorFor<Customer>
{
    public override ValidationResult Validate(Customer instance)
    {
        var failures = new List<ValidationFailure>();

        // Direct rules
        if (string.IsNullOrEmpty(instance.Name))
            failures.Add(new ValidationFailure { PropertyName = "Name", ErrorMessage = "Name must not be empty." });

        // Nested — skipped if null, dot-prefixed if not
        if (instance.Address is not null)
        {
            var nestedResult = new AddressValidator().Validate(instance.Address);
            foreach (var f in nestedResult.Failures)
                failures.Add(new ValidationFailure
                {
                    PropertyName = "Address." + f.PropertyName,
                    ErrorMessage = f.ErrorMessage
                });
        }

        return new ValidationResult(failures.ToArray());
    }
}

// Generated for Address (flat → keeps fixed array)
public sealed partial class AddressValidator : ValidatorFor<Address>
{
    public override ValidationResult Validate(Address instance)
    {
        var buffer = new ValidationFailure[2];
        int count = 0;

        if (string.IsNullOrEmpty(instance.Street))
            buffer[count++] = new ValidationFailure { PropertyName = "Street", ErrorMessage = "Street must not be empty." };

        if (string.IsNullOrEmpty(instance.City))
            buffer[count++] = new ValidationFailure { PropertyName = "City", ErrorMessage = "City must not be empty." };

        return new ValidationResult(buffer[..count].ToArray());
    }
}
```

---

## 3. Property Name Convention

Nested failures are prefixed with the parent property name using dot notation:

| Failing property | `PropertyName` in failure |
|---|---|
| `Address.Street` | `"Address.Street"` |
| `Address.City` | `"Address.City"` |
| `Order.Address.Street` | `"Order.Address.Street"` |

Arbitrarily deep nesting works automatically — each level applies the same dot-prefix pattern.

---

## Key Decisions

| Decision | Choice | Reason |
|---|---|---|
| Declaration | Automatic (no attribute) | Convention-driven, consistent with `[Validate]` opt-in |
| Null handling | Skip silently | Pair with `[NotNull]` explicitly; no magic failures |
| Property name format | Dot-separated path | Matches ASP.NET model state keys, widely understood |
| Buffer strategy for nested models | `List<ValidationFailure>` | Unknown failure count at compile time; flat models unaffected |
| Nested validator instantiation | `new {Type}Validator()` | Simple; DI via partial class escape hatch if needed |
