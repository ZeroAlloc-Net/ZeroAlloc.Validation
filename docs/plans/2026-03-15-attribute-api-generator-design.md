# Attribute API & Source Generator Design

**Date:** 2026-03-15
**Status:** Approved

---

## Overview

ZValidation uses attribute-based rule declarations on model classes. A Roslyn incremental source generator reads the attributes and emits a zero-allocation `Validate()` implementation. No reflection at runtime.

---

## 1. Opt-In Model Declaration

Models opt in with `[Validate]`. Properties declare rules with attribute annotations in the `ZValidation` namespace.

```csharp
using ZValidation;

[Validate]
public class Customer
{
    [NotEmpty(Message = "Name is required.")]
    [MaxLength(100)]
    public string Name { get; set; }

    [NotEmpty]
    [EmailAddress]
    public string Email { get; set; }

    [GreaterThan(0)]
    [LessThan(120)]
    public int Age { get; set; }

    [InclusiveBetween(0.0, 999.99)]
    public decimal Balance { get; set; }
}
```

- Multiple rule attributes on one property are evaluated in **declaration order**
- Evaluation **stops at first failure per property** — subsequent rules on the same property are skipped
- All properties are always evaluated (failures accumulate across properties)
- Custom error message override per rule via `Message` parameter

---

## 2. Generator Behavior

Triggers on any class with `[Validate]`. For each such class:

1. Enumerate all properties
2. Collect rule attributes per property in declaration order
3. Count total rules across all properties → `stackalloc` buffer size (compile-time constant)
4. Emit `sealed partial class {ClassName}Validator : ValidatorFor<{ClassName}>` in the same namespace as the model

**Escape hatch:** If a `partial class {ClassName}Validator` already exists in the compilation (user-defined), the generator emits into that existing partial class instead of creating a new one. This allows users to add constructor injection and custom validation methods alongside the generated `Validate()` override.

```csharp
// User-defined partial — adds DI and custom logic
public partial class CustomerValidator
{
    private readonly IRepository _repo;
    public CustomerValidator(IRepository repo) => _repo = repo;
}

// Generator-emitted partial — adds Validate() override
public sealed partial class CustomerValidator : ValidatorFor<Customer>
{
    public override ValidationResult Validate(Customer instance) { ... }
}
```

---

## 3. Generated Code Shape

```csharp
// Generated for Customer with N total rules
public sealed partial class CustomerValidator : ValidatorFor<Customer>
{
    public override ValidationResult Validate(Customer instance)
    {
        Span<ValidationFailure> buffer = stackalloc ValidationFailure[N];
        int count = 0;

        // Name — stop at first failure
        if (string.IsNullOrEmpty(instance.Name))
            buffer[count++] = new ValidationFailure { PropertyName = "Name", ErrorMessage = "Name is required." };
        else if (instance.Name.Length > 100)
            buffer[count++] = new ValidationFailure { PropertyName = "Name", ErrorMessage = "Must not exceed 100 characters." };

        // Email — stop at first failure
        if (string.IsNullOrEmpty(instance.Email))
            buffer[count++] = new ValidationFailure { PropertyName = "Email", ErrorMessage = "Must not be empty." };
        else if (!EmailValidator.IsValid(instance.Email))
            buffer[count++] = new ValidationFailure { PropertyName = "Email", ErrorMessage = "Must be a valid email address." };

        // Age — stop at first failure
        if (instance.Age <= 0)
            buffer[count++] = new ValidationFailure { PropertyName = "Age", ErrorMessage = "Must be greater than 0." };
        else if (instance.Age >= 120)
            buffer[count++] = new ValidationFailure { PropertyName = "Age", ErrorMessage = "Must be less than 120." };

        // Balance
        if (instance.Balance < 0.0m || instance.Balance > 999.99m)
            buffer[count++] = new ValidationFailure { PropertyName = "Balance", ErrorMessage = "Must be between 0.00 and 999.99." };

        return new ValidationResult(buffer[..count].ToArray());
    }
}
```

**Allocation characteristics:**
- `stackalloc ValidationFailure[N]` — stack allocated, zero heap cost
- Buffer sized to exact rule count at compile time — no over-allocation
- `buffer[..count].ToArray()` — only allocates on the **unhappy path** (when there are failures)
- Happy path (`IsValid == true`) allocates nothing beyond the stack buffer

---

## 4. Initial Built-in Rule Attributes

First iteration — 9 attributes covering the most common validation needs:

| Attribute | Applies to | Generated check |
|-----------|------------|-----------------|
| `[NotNull]` | reference types | `instance.Prop is null` |
| `[NotEmpty]` | `string`, collections | `string.IsNullOrEmpty` / `Count == 0` |
| `[MinLength(n)]` | `string` | `instance.Prop.Length < n` |
| `[MaxLength(n)]` | `string` | `instance.Prop.Length > n` |
| `[GreaterThan(n)]` | numeric, `IComparable` | `instance.Prop <= n` |
| `[LessThan(n)]` | numeric, `IComparable` | `instance.Prop >= n` |
| `[InclusiveBetween(min, max)]` | numeric, `IComparable` | `instance.Prop < min \|\| instance.Prop > max` |
| `[EmailAddress]` | `string` | simple format check (no regex heap alloc) |
| `[Matches(pattern)]` | `string` | `Regex.IsMatch` (user owns the Regex instance) |

All attributes expose an optional `Message` property for custom error messages. Default messages are hardcoded strings in the generator (no resource lookup at runtime).

---

## 5. Attribute Base Class

All rule attributes derive from a common base to allow the generator to discover them reliably:

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class ValidationAttribute : Attribute
{
    public string? Message { get; set; }
}
```

`AllowMultiple = true` allows stacking multiple rules on one property.

---

## Key Decisions

| Decision | Choice | Reason |
|----------|--------|--------|
| Rule declaration style | Attributes on model | Clean, no ceremony, discoverable |
| Opt-in mechanism | `[Validate]` on class | Explicit, no surprise generation |
| Validator naming | `{ClassName}Validator`, auto | Zero ceremony default |
| Customization escape hatch | User partial class | Enables DI + custom rules without forking |
| Per-property cascade | Stop at first failure | Less noise, more actionable errors |
| Cross-property cascade | Always continue | All properties validated in one pass |
| Buffer allocation | `stackalloc[N]` sized at compile time | Zero heap on hot path |
| Failure allocation | `ToArray()` on unhappy path only | Allocates only when there are errors |
