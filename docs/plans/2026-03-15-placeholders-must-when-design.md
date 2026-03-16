# Placeholders, `[Must]`, and `[When]`/`[Unless]` Design

**Date:** 2026-03-15
**Status:** Approved

---

## Overview

Three attribute-system enhancements that compose naturally:

1. **Message placeholders** — gen-time token substitution in `Message` strings
2. **`[Must]`** — custom predicate attribute referencing an instance method on the model
3. **`When` / `Unless`** — conditional rule execution via named params on `ValidationAttribute`

All changes are generator-only or attribute-only — no runtime overhead beyond what already exists.

---

## 1. Message Placeholders

### Supported tokens (all resolved at code-gen time → inlined string literals)

| Token | Resolved from |
|---|---|
| `{PropertyName}` | Property name (always known at gen time) |
| `{ComparisonValue}` | First constructor arg — used by `[Equal]`, `[NotEqual]`, `[GreaterThan]`, `[LessThan]`, `[GreaterThanOrEqualTo]`, `[LessThanOrEqualTo]` |
| `{MinLength}` | Min constructor arg — used by `[Length]`, `[MinLength]` |
| `{MaxLength}` | Max constructor arg — used by `[Length]`, `[MaxLength]` |
| `{From}` | Min constructor arg — used by `[ExclusiveBetween]`, `[InclusiveBetween]` |
| `{To}` | Max constructor arg — used by `[ExclusiveBetween]`, `[InclusiveBetween]` |

`{PropertyValue}` is explicitly **out of scope** (requires runtime string allocation).

### Generator change

Replace `GetMessage(attr)` with `ResolveMessage(attr, fqn, propName)`:

```csharp
private static string? ResolveMessage(AttributeData attr, string fqn, string propName)
{
    var raw = GetMessage(attr);
    if (raw is null) return null;

    return raw
        .Replace("{PropertyName}", propName)
        .Replace("{ComparisonValue}", GetComparisonValueString(fqn, attr))
        .Replace("{MinLength}",      GetMinLengthString(fqn, attr))
        .Replace("{MaxLength}",      GetMaxLengthString(fqn, attr))
        .Replace("{From}",           GetFromString(fqn, attr))
        .Replace("{To}",             GetToString(fqn, attr));
}
```

Helper methods return `null` when the token doesn't apply to that FQN; `Replace` with `null` leaves the token as-is (safe fallback).

### Token-to-FQN mapping

| Token helper | Applicable FQNs | Source |
|---|---|---|
| `GetComparisonValueString` | `EqualFqn`, `NotEqualFqn`, `GreaterThanFqn`, `LessThanFqn`, `GreaterThanOrEqualToFqn`, `LessThanOrEqualToFqn` | `ConstructorArguments[0]` |
| `GetMinLengthString` | `LengthFqn`, `MinLengthFqn` | `ConstructorArguments[0]` |
| `GetMaxLengthString` | `LengthFqn`, `MaxLengthFqn` | `ConstructorArguments[1]` (Length) or `[0]` (MaxLength) |
| `GetFromString` | `ExclusiveBetweenFqn`, `InclusiveBetweenFqn` | `ConstructorArguments[0]` |
| `GetToString` | `ExclusiveBetweenFqn`, `InclusiveBetweenFqn` | `ConstructorArguments[1]` |

### Example

```csharp
[GreaterThan(0, Message = "'{PropertyName}' must be > {ComparisonValue}")]
public int Age { get; set; }
// generator emits error message: "'Age' must be > 0"  ← pure string literal
```

---

## 2. `[Must]` Attribute

### New attribute

```csharp
// src/ZeroAlloc.Validation/Attributes/MustAttribute.cs
namespace ZeroAlloc.Validation;

public sealed class MustAttribute(string methodName) : ValidationAttribute
{
    public string MethodName { get; } = methodName;
}
```

### Method contract

The referenced method must be an **instance method on the model class** with signature:

```csharp
bool MethodName(PropertyType value)
```

### Generator changes

- FQN constant: `private const string MustFqn = "ZeroAlloc.Validation.MustAttribute";`
- Register in `IsRuleAttribute`
- `BuildCondition` case:
  ```csharp
  MustFqn => $"!{modelParamName}.{GetStringArg(attr, 0)}({access})",
  ```
  where `modelParamName` is the generated `instance` parameter name.
- Default message: `"{PropertyName} is invalid"`

### Example

```csharp
[Validate]
public partial class Customer
{
    [Must(nameof(NameStartsWithA), Message = "'{PropertyName}' must start with A")]
    public string Name { get; set; }

    private bool NameStartsWithA(string value) => value.StartsWith("A", StringComparison.Ordinal);
}
// emits: if (!instance.NameStartsWithA(instance.Name)) failures.Add(...)
```

---

## 3. `When` / `Unless` Named Params

### Attribute base class change

```csharp
// src/ZeroAlloc.Validation/Attributes/ValidationAttribute.cs
public abstract class ValidationAttribute : Attribute
{
    public string? Message { get; set; }
    public string? When    { get; set; }   // instance method name: bool MethodName()
    public string? Unless  { get; set; }   // instance method name: bool MethodName()
}
```

### Method contract

The referenced method must be an **instance method on the model class** with signature:

```csharp
bool MethodName()
```

No parameters — the method accesses model state via `this`.

### Generator change

Wrap the existing emitted `if (failureCondition)` with the guard. Current emission:

```csharp
if ({failureCondition})
    failures.Add(...);
```

With `When = "X"` and/or `Unless = "Y"`:

```csharp
if ({whenGuard}{unlessGuard}{failureCondition})
    failures.Add(...);
```

where:
- `whenGuard`   = `"instance.X() && "` when `When` is set, else `""`
- `unlessGuard` = `"!instance.Y() && "` when `Unless` is set, else `""`

Both can be combined (AND semantics): `When` must be true **and** `Unless` must be false.

### Example

```csharp
[Validate]
public partial class Order
{
    public bool RequiresShipping { get; set; }

    [NotNull(When = nameof(ShippingRequired), Message = "Shipping address is required")]
    public Address? ShippingAddress { get; set; }

    private bool ShippingRequired() => RequiresShipping;
}
// emits: if (instance.ShippingRequired() && instance.ShippingAddress is null)
//            failures.Add(...)
```

---

## 4. Testing

### Generator tests (`GeneratorRuleEmissionTests.cs`)

- Placeholder: `{PropertyName}` replaced in emitted message
- Placeholder: `{ComparisonValue}` replaced for `[GreaterThan]`
- Placeholder: `{From}` / `{To}` replaced for `[ExclusiveBetween]`
- Placeholder: `{MinLength}` / `{MaxLength}` replaced for `[Length]`
- `[Must]` → emitted condition contains `!instance.MethodName(`
- `[When]` → emitted condition prefixed with `instance.CondMethod() &&`
- `[Unless]` → emitted condition prefixed with `!instance.CondMethod() &&`
- Combined `[When]` + `[Unless]` → both guards present

### Integration tests (`EndToEndTests.cs`)

- Placeholder group: custom message with `{PropertyName}` and `{ComparisonValue}` appears verbatim in `ValidationFailure.ErrorMessage`
- `[Must]` group: valid value passes, invalid fails with correct `PropertyName` and message
- `[When]` group: rule skipped when condition false, triggered when condition true
- `[Unless]` group: rule triggered when condition false, skipped when condition true
- Combined group: `[When]` + `[Unless]` both guards respected

---

## Key Decisions

| Decision | Choice | Reason |
|---|---|---|
| `{PropertyValue}` | Out of scope | Requires runtime string allocation; contradicts zero-alloc goal |
| `Must` method signature | `bool M(PropertyType value)` | Explicit about what's validated; composable |
| `When`/`Unless` method signature | `bool M()` (no args) | Zero allocation; model accesses own state via `this` |
| `When`/`Unless` placement | Named params on `ValidationAttribute` | Consistent with `Message`; no new attribute classes |
| Both `When` and `Unless` | AND semantics | Most predictable; both must be satisfied |
