# Collection Validation Design

**Date:** 2026-03-15
**Status:** Approved

---

## Overview

When a collection property's element type is decorated with `[Validate]`, the generator automatically includes per-element validation in the parent validator. No new attributes required — convention-driven, consistent with nested object validation.

---

## 1. Declaration

No extra attributes. The generator detects collection properties whose element type carries `[Validate]` automatically:

```csharp
[Validate]
public class LineItem
{
    [NotEmpty]
    public string Sku { get; set; } = "";

    [GreaterThan(0)]
    public int Quantity { get; set; }
}

[Validate]
public class Order
{
    [NotEmpty]
    public string Reference { get; set; } = "";

    // LineItem has [Validate] → each element validated automatically, bracket-indexed failures
    public List<LineItem> LineItems { get; set; } = [];

    // string has no [Validate] → not iterated
    public List<string> Tags { get; set; } = [];
}
```

**Supported collection types:** `T[]`, `List<T>`, `IEnumerable<T>`, `ICollection<T>`, `IList<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` — any type that implements `IEnumerable<T>` where T has `[Validate]`, plus arrays.

**Null handling:**
- Null collection → silently skipped. Use `[NotNull]` on the property if null should fail.
- Null element → silently skipped. Use `[NotNull]` on element properties explicitly.

---

## 2. Generated Code Shape

The emitter switches to `List<ValidationFailure>` (same trigger as nested objects) and adds a foreach loop with an index counter:

```csharp
// Generated for Order
public sealed partial class OrderValidator : ValidatorFor<Order>
{
    public override ValidationResult Validate(Order instance)
    {
        var failures = new System.Collections.Generic.List<global::ZeroAlloc.Validation.ValidationFailure>();

        // Direct rules
        if (string.IsNullOrEmpty(instance.Reference))
            failures.Add(new global::ZeroAlloc.Validation.ValidationFailure { PropertyName = "Reference", ErrorMessage = "Reference must not be empty." });

        // Collection — skipped if null, element skipped if null
        if (instance.LineItems is not null)
        {
            int _lineItemsIdx = 0;
            foreach (var _lineItemsItem in instance.LineItems)
            {
                if (_lineItemsItem is not null)
                {
                    var _lineItemsResult = new global::ZeroAlloc.Validation.Tests.Integration.LineItemValidator().Validate(_lineItemsItem);
                    foreach (var f in _lineItemsResult.Failures)
                        failures.Add(new global::ZeroAlloc.Validation.ValidationFailure
                        {
                            PropertyName = "LineItems[" + _lineItemsIdx + "]." + f.PropertyName,
                            ErrorMessage = f.ErrorMessage
                        });
                }
                _lineItemsIdx++;
            }
        }

        return new global::ZeroAlloc.Validation.ValidationResult(failures.ToArray());
    }
}
```

---

## 3. Property Name Convention

Collection element failures are prefixed with the parent property name and the zero-based element index in bracket notation:

| Failing property | `PropertyName` in failure |
|---|---|
| `LineItems[0].Sku` | `"LineItems[0].Sku"` |
| `LineItems[2].Quantity` | `"LineItems[2].Quantity"` |
| `Order.LineItems[0].Sku` | `"Order.LineItems[0].Sku"` (when Order is itself nested) |

Arbitrary nesting chains correctly — the existing dot-prefix pattern from nested object validation composes with the bracket-index pattern automatically.

---

## Key Decisions

| Decision | Choice | Reason |
|---|---|---|
| Declaration | Automatic (no attribute) | Convention-driven, consistent with nested object detection |
| Supported types | Array + any `IEnumerable<T>` implementor | Covers all common .NET collection shapes |
| Null collection | Skip silently | Same behaviour as null nested object |
| Null element | Skip silently | Pair with `[NotNull]` on element explicitly; no magic failures |
| Index format | `[n]` bracket notation | ASP.NET model binding standard, frontend framework compatible |
| Buffer strategy | `List<ValidationFailure>` | Element count unknown at compile time; flat models unaffected |
| Element validator instantiation | `new {Type}Validator()` (fully qualified) | Consistent with nested object pattern |
