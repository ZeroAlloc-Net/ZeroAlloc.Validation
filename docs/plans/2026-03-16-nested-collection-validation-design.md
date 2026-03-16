# Design: Nested Object & Collection Validation

**Date:** 2026-03-16
**Status:** Approved

---

## Overview

Add support for validating nested objects and collections of objects by automatically composing validators when the nested type carries `[Validate]`, with an explicit `[ValidateWith]` escape hatch for types the user doesn't control.

---

## Section 1: Triggering Nested / Collection Validation

### Auto-compose (primary path)

When a property's type (or element type for collections) is decorated with `[Validate]`, the generator automatically emits a delegation call — no attribute on the property is needed.

```csharp
[Validate]
public partial class Customer
{
    public Address     ShippingAddress { get; set; }  // auto — Address has [Validate]
    public List<OrderLine> Lines       { get; set; }  // auto — OrderLine has [Validate]
    public OrderLine[] Tags            { get; set; }  // arrays also supported
}
```

Supported collection types: `IEnumerable<T>`, `ICollection<T>`, `IList<T>`, `List<T>`, `T[]`.

### Explicit override (escape hatch)

For types the user doesn't control (third-party, no `[Validate]`), use `[ValidateWith]`:

```csharp
[ValidateWith(typeof(ThirdPartyAddressValidator))]
public ThirdPartyAddress BillingAddress { get; set; }
```

### Null handling

Null nested objects and null collections are skipped silently. Add `[NotNull]` explicitly to make null a validation failure:

```csharp
[NotNull]
public Address ShippingAddress { get; set; }
```

### Analyzers

| Code | Severity | Description |
|------|----------|-------------|
| **ZV0011** | Warning | `[ValidateWith]` on a property whose type already has `[Validate]` — the attribute is redundant, remove it |
| **ZV0012** | Error | `[ValidateWith(typeof(T))]` where `T` does not implement `ValidatorFor<TProperty>` for the correct property type |

---

## Section 2: Property Paths and Failure Prefixing

Failures from nested validators are re-emitted with a prefixed `PropertyName`:

| Scenario | `PropertyName` in failure |
|---|---|
| Single nested | `ShippingAddress.Street` |
| Collection element | `Lines[0].Quantity` |
| Double-nested | `ShippingAddress.Country.Code` |

The prefix (e.g. `"ShippingAddress."`) is a compile-time string literal emitted by the generator. Combining it with the nested failure's `PropertyName` requires one string allocation per nested failure — an explicit trade-off. Simple property validation (no nesting) remains zero-alloc.

Generated code shape:

```csharp
// Single nested object
if (instance.ShippingAddress is not null)
{
    var nested = _shippingAddressValidator.Validate(instance.ShippingAddress);
    foreach (ref readonly var f in nested.Failures)
        Add(new ValidationFailure("ShippingAddress." + f.PropertyName, f.ErrorMessage));
}

// Collection
for (int i = 0; i < instance.Lines.Count; i++)
{
    var nested = _orderLineValidator.Validate(instance.Lines[i]);
    foreach (ref readonly var f in nested.Failures)
        Add(new ValidationFailure($"Lines[{i}]." + f.PropertyName, f.ErrorMessage));
}
```

---

## Section 3: Constructor Injection

The generator emits a constructor on the parent validator for each required nested validator. ZeroAlloc.Inject resolves them automatically.

```csharp
// Generated
public sealed partial class CustomerValidator : ValidatorFor<Customer>
{
    private readonly AddressValidator      _shippingAddressValidator;
    private readonly OrderLineValidator    _orderLineValidator;

    public CustomerValidator(
        AddressValidator   shippingAddressValidator,
        OrderLineValidator orderLineValidator)
    {
        _shippingAddressValidator = shippingAddressValidator;
        _orderLineValidator       = orderLineValidator;
    }
}
```

- For `[ValidateWith(typeof(T))]`, the injected type is `T` instead of the auto-derived `{TypeName}Validator`.
- If a model has no nested properties, no constructor is emitted (existing behaviour preserved).

---

## Section 4: Testing

Tests construct the parent validator directly, passing nested validators via the generated constructor:

```csharp
var validator = new CustomerValidator(
    new AddressValidator(),
    new OrderLineValidator());

var result = validator.Validate(customer);
ValidationAssert.HasError(result, "ShippingAddress.Street");
ValidationAssert.HasError(result, "Lines[0].Quantity");
```

`ValidationAssert.HasError` already accepts arbitrary property name strings — no changes needed.

Generator unit tests (in-memory Roslyn driver) verify:
- Constructor parameters emitted for each nested/collection property
- Null guard emitted before delegation
- Prefix string literal is correct (`"ShippingAddress."`, `"Lines[0]."`)
- No constructor emitted when model has no nested properties
- ZV0011 diagnostic fires for redundant `[ValidateWith]`
- ZV0012 diagnostic fires for type mismatch in `[ValidateWith]`

---

## Out of Scope

- `IDictionary<TKey, TValue>` collection support (future)
- Async nested validation
- Per-element index customization in failure messages
