# Display Name Override + Validator-Level Cascade Design

## Goal

Two independent features shipped together:
1. **§5.5 Display Name Override** — allow a human-readable name to replace the raw C# property name in all error messages
2. **§7.2 Validator-Level Cascade** — stop validating further properties after the first one produces any failure ("fail-fast" mode at the validator level)

---

## Feature 1: Display Name Override (`[DisplayName]`)

### API

A new `[DisplayName("...")]` attribute applied at the property level. One declaration covers all rules on that property.

```csharp
[DisplayName("First Name")]
[NotEmpty]
[MinLength(2)]
public string Forename { get; set; } = "";
// → "First Name must not be empty."
// → "First Name must be at least 2 characters."

[DisplayName("ZIP Code")]
[Matches(@"^\d{5}$", Message = "{PropertyName} must be 5 digits.")]
public string ZipCode { get; set; } = "";
// → "ZIP Code must be 5 digits."
```

If `[DisplayName]` is absent, behavior is unchanged — raw C# property name used as today.

### Architecture

- New attribute class: `src/ZeroAlloc.Validation/Attributes/DisplayNameAttribute.cs` (`AttributeTargets.Property`, single-use)
- `RuleEmitter.cs`: add `GetDisplayName(IPropertySymbol prop)` helper that reads the attribute; update `ResolveMessage` and `GetDefaultMessage` to use the display name instead of the raw property name for `{PropertyName}` substitution and default message generation

### Behavior

- Replaces the raw property name in the `{PropertyName}` placeholder in custom `Message` strings
- Replaces the raw property name in all generated default messages for that property
- Does **not** affect `PropertyName` in `ValidationFailure` — that stays the C# property name for programmatic use
- Does **not** affect nested path prefixes (e.g. `"ShippingAddress.Street"` — the `ShippingAddress` segment is the property name, not a display name)

### Files changed

- Create: `src/ZeroAlloc.Validation/Attributes/DisplayNameAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

---

## Feature 2: Validator-Level Cascade (`[Validate(StopOnFirstFailure = true)]`)

### API

A `bool StopOnFirstFailure` parameter on the existing `[Validate]` attribute. No new attribute needed.

```csharp
[Validate(StopOnFirstFailure = true)]
public class Order
{
    [NotEmpty]
    public string Reference { get; set; } = "";       // fails → returns immediately

    [NotNull]
    public Address? ShippingAddress { get; set; }     // only reached if Reference passed
}
```

### Architecture

- Modify `ValidateAttribute.cs` to add `bool StopOnFirstFailure { get; set; }` property
- `RuleEmitter.cs`: detect the flag; in the generated `Validate` body, emit a failure-count check after each property group (direct rules + nested/collection for that property); if count increased, return early

### Behavior

- Properties processed in declaration order (unchanged from today)
- After each property group, if `failures.Count > countBefore` (nested path) or `count > countBefore` (flat path): return immediately
- A failure from a nested validator counts as the property failing — consistent with FluentValidation's behavior
- Composes correctly with property-level `[StopOnFirstFailure]`: both can be active simultaneously
- Default (`StopOnFirstFailure = false`) is unchanged — all properties always validated

### Generated code shape (nested path)

```csharp
// For each property group:
int _b0 = failures.Count;
if (string.IsNullOrEmpty(instance.Reference))
    failures.Add(...);
if (failures.Count > _b0) return new ValidationResult(failures.ToArray());

int _b1 = failures.Count;
if (instance.ShippingAddress is null)
    failures.Add(...);
if (instance.ShippingAddress is not null) { /* nested validator */ }
if (failures.Count > _b1) return new ValidationResult(failures.ToArray());
```

### Generated code shape (flat path)

```csharp
int _b0 = count;
if (...) buffer[count++] = ...;
if (count > _b0) { var r = new ValidationFailure[count]; Array.Copy(buffer, r, count); return new ValidationResult(r); }
```

### Files changed

- Modify: `src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

---

## Testing

Both features need:

### Generator emission tests
- `[DisplayName]` present → emitted message uses display name, not property name
- `[DisplayName]` absent → unchanged (regression guard)
- `StopOnFirstFailure = true` → emitted code contains early-return check after each property group
- `StopOnFirstFailure = false` (default) → no early-return checks emitted (regression guard)

### Integration tests
- Display name appears correctly in default messages
- Display name appears correctly in `{PropertyName}` placeholder
- `ValidationFailure.PropertyName` still uses the raw C# name (not the display name)
- Validator-level cascade: second property not validated when first fails
- Validator-level cascade: both properties validated when first passes
- Validator-level cascade with nested validator: nested failure stops further properties
- Both features active simultaneously: correct behavior
