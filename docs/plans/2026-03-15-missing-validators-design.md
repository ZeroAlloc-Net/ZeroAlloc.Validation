# Missing Built-in Validators Design

**Date:** 2026-03-15
**Status:** Approved

---

## Overview

Add the 11 missing built-in validation attributes to complete the attribute set. All follow the existing pattern: a C# attribute class in `src/ZValidation/Attributes/`, a FQN constant + `BuildCondition` + `GetDefaultMessage` case in `RuleEmitter.cs`, and no runtime overhead beyond the already-established helpers.

---

## 1. New Attributes

| Attribute | Constructor args | Purpose |
|---|---|---|
| `[Null]` | none | Value must be null |
| `[Empty]` | none | String must be null or empty |
| `[Equal(double)]` / `[Equal(string)]` | double or string | Value must equal constant |
| `[NotEqual(double)]` / `[NotEqual(string)]` | double or string | Value must not equal constant |
| `[GreaterThanOrEqualTo(double)]` | double | Value ≥ n |
| `[LessThanOrEqualTo(double)]` | double | Value ≤ n |
| `[ExclusiveBetween(double, double)]` | min, max (doubles) | min < value < max |
| `[Length(int, int)]` | min, max (ints) | String length within [min, max] |
| `[IsInEnum]` | none | Enum value is defined in its type |
| `[IsEnumName(Type)]` | Type (the enum type) | String is a valid member name of the given enum |
| `[PrecisionScale(int, int)]` | precision, scale (ints) | Decimal fits within precision/scale |

All attributes inherit `ValidationAttribute` and support the `Message` named parameter, identical to existing attributes.

`Equal` and `NotEqual` have two constructor overloads — one taking `double` (for numeric properties) and one taking `string` (for string properties). The generator distinguishes them via `ConstructorArguments[0].Type.SpecialType == SpecialType.System_String`.

---

## 2. Generator Changes (`RuleEmitter.cs`)

### New FQN constants

```csharp
private const string NullFqn                 = "ZValidation.NullAttribute";
private const string EmptyFqn                = "ZValidation.EmptyAttribute";
private const string EqualFqn                = "ZValidation.EqualAttribute";
private const string NotEqualFqn             = "ZValidation.NotEqualAttribute";
private const string GreaterThanOrEqualToFqn = "ZValidation.GreaterThanOrEqualToAttribute";
private const string LessThanOrEqualToFqn    = "ZValidation.LessThanOrEqualToAttribute";
private const string ExclusiveBetweenFqn     = "ZValidation.ExclusiveBetweenAttribute";
private const string LengthFqn               = "ZValidation.LengthAttribute";
private const string IsInEnumFqn             = "ZValidation.IsInEnumAttribute";
private const string IsEnumNameFqn           = "ZValidation.IsEnumNameAttribute";
private const string PrecisionScaleFqn       = "ZValidation.PrecisionScaleAttribute";
```

### Emitted conditions

| FQN | Emitted condition (failure = true) |
|---|---|
| `NullFqn` | `{access} is not null` |
| `EmptyFqn` | `!string.IsNullOrEmpty({access})` |
| `EqualFqn` (string) | `{access} != "{value}"` |
| `EqualFqn` (double) | `System.Convert.ToDouble({access}) != {value}` |
| `NotEqualFqn` (string) | `{access} == "{value}"` |
| `NotEqualFqn` (double) | `System.Convert.ToDouble({access}) == {value}` |
| `GreaterThanOrEqualToFqn` | `System.Convert.ToDouble({access}) < {n}` |
| `LessThanOrEqualToFqn` | `System.Convert.ToDouble({access}) > {n}` |
| `ExclusiveBetweenFqn` | `System.Convert.ToDouble({access}) <= {min} \|\| System.Convert.ToDouble({access}) >= {max}` |
| `LengthFqn` | `{access}.Length < {min} \|\| {access}.Length > {max}` |
| `IsInEnumFqn` | `!System.Enum.IsDefined(typeof({FullPropType}), {access})` |
| `IsEnumNameFqn` | `!System.Enum.IsDefined(typeof({EnumTypeArg}), {access})` |
| `PrecisionScaleFqn` | `global::ZValidationInternal.DecimalValidator.ExceedsPrecisionScale({access}, {precision}, {scale})` |

`IsInEnum` reads the property's type name from the Roslyn `IPropertySymbol` at generation time. For nullable enums (`Status?`), unwrap to the underlying type using `INamedTypeSymbol.TypeArguments[0]`.

`IsEnumName` reads the target enum type from `ConstructorArguments[0]` as a `ITypeSymbol` and emits its fully qualified name.

---

## 3. New Internal Helper

**`src/ZValidation/Internal/DecimalValidator.cs`**

```csharp
namespace ZValidationInternal;

internal static class DecimalValidator
{
    internal static bool ExceedsPrecisionScale(decimal value, int precision, int scale)
    {
        // Use decimal.GetBits() — zero allocation
        var bits = decimal.GetBits(value);
        int exponent = (bits[3] >> 16) & 0x1F;  // scale (decimal places)
        if (exponent > scale) return true;

        // Count total significant digits
        var abs = decimal.Truncate(decimal.Abs(value));
        int integerDigits = abs == 0 ? 0 : (int)Math.Floor(Math.Log10((double)abs)) + 1;
        if (integerDigits + scale > precision) return true;

        return false;
    }
}
```

No other new helpers — `[CreditCard]` was dropped in favour of the existing `[Matches(pattern)]`.

---

## 4. Testing

**Generator tests** (`GeneratorRuleEmissionTests.cs`) — one `[Fact]` per new validator verifying the key emitted substring:
- `[Null]` → `"is not null"`
- `[Empty]` → `"IsNullOrEmpty"`
- `[Equal]` (double) → `"!= 42"` (or similar)
- `[GreaterThanOrEqualTo]` → `"< 0"`
- `[ExclusiveBetween]` → `"<= "` and `">= "`
- `[Length]` → `".Length < "` and `".Length > "`
- `[IsInEnum]` → `"IsDefined"`
- `[IsEnumName]` → `"IsDefined"`
- `[PrecisionScale]` → `"ExceedsPrecisionScale"`

**Integration tests** (`EndToEndTests.cs`) — grouped model+test classes per logical group:
- Null/Empty group: `[Null]`, `[Empty]`
- Equality group: `[Equal]`, `[NotEqual]` (string and double)
- Range group: `[GreaterThanOrEqualTo]`, `[LessThanOrEqualTo]`, `[ExclusiveBetween]`
- Length group: `[Length]`
- Enum group: `[IsInEnum]`, `[IsEnumName]`
- Decimal group: `[PrecisionScale]`

Each group tests: valid value passes, boundary value fails with correct `PropertyName`, and custom `Message` is propagated.

---

## Key Decisions

| Decision | Choice | Reason |
|---|---|---|
| `Equal`/`NotEqual` scope | Constants only (double + string) | Cross-property equality needs its own design session |
| `CreditCard` | Dropped | `[Matches(pattern)]` covers it; no need for a Luhn helper |
| `Empty` scope | Strings only | Matches existing `NotEmpty` scope; collection emptiness is separate |
| `IsInEnum` type resolution | From Roslyn property symbol at gen time | No runtime reflection; AOT safe |
| `PrecisionScale` helper | `decimal.GetBits()` — zero allocation | Consistent with zero-alloc design goal |
| Nullable enum unwrapping | `INamedTypeSymbol.TypeArguments[0]` | Handles `Status?` transparently |
