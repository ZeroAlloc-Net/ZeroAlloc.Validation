---
id: testing
title: Testing
slug: /docs/testing
description: Test validators with ValidationAssert helpers from the ZeroAlloc.Validation.Testing package.
sidebar_position: 8
---

## Overview

The `ZeroAlloc.Validation.Testing` NuGet package provides `ValidationAssert` — a set of static helpers for writing clean, readable validator tests.

Install:
```bash
dotnet add package ZeroAlloc.Validation.Testing
```

## ValidationAssert methods

```csharp
// Passes if result has any failure for propertyName; throws ValidationAssertException otherwise
ValidationAssert.HasError(result, "Email");

// Passes if result has a failure for propertyName with exactly that message; throws otherwise
ValidationAssert.HasErrorWithMessage(result, "Email", "Email must be a valid email address.");

// Passes if result.IsValid is true (zero failures); throws otherwise
ValidationAssert.NoErrors(result);
```

`ValidationAssertException` (extends `Exception`) is thrown on assertion failure with a descriptive message. Because the exception type is framework-agnostic, `ValidationAssert` works with any test framework — xUnit, NUnit, MSTest, or otherwise.

## Example test — flat model

```csharp
using ZeroAlloc.Validation.Testing;
using Xunit;

public class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    [Fact]
    public void Valid_order_passes()
    {
        var request = new CreateOrderRequest
        {
            Reference = "ORD-001",
            Amount = 100m,
            Email = "user@example.com"
        };

        var result = _validator.Validate(request);
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Empty_email_fails()
    {
        var request = new CreateOrderRequest { Reference = "ORD-001", Amount = 100m, Email = "" };
        var result = _validator.Validate(request);

        ValidationAssert.HasError(result, "Email");
    }

    [Fact]
    public void Invalid_email_format_fails_with_message()
    {
        var request = new CreateOrderRequest { Reference = "ORD-001", Amount = 100m, Email = "not-an-email" };
        var result = _validator.Validate(request);

        ValidationAssert.HasErrorWithMessage(result, "Email", "Email must be a valid email address.");
    }
}
```

## Example test — nested model

Nested validator instances are passed manually via constructor injection:

```csharp
public class OrderValidatorTests
{
    private readonly OrderValidator _validator = new(new AddressValidator());

    [Fact]
    public void Null_address_fails()
    {
        var order = new Order { Reference = "ORD-001", ShippingAddress = null };
        var result = _validator.Validate(order);

        ValidationAssert.HasError(result, "ShippingAddress");
    }

    [Fact]
    public void Nested_property_failure_is_prefixed()
    {
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = new Address { Street = "", City = "London", PostalCode = "EC1A 1BB" }
        };

        var result = _validator.Validate(order);
        ValidationAssert.HasError(result, "ShippingAddress.Street");
    }
}
```

Failures from a nested validator are prefixed with the parent property name, so `Street` inside `ShippingAddress` becomes `ShippingAddress.Street`.

## Example test — collection model

Collection item failures are prefixed with the collection property name and the zero-based index of the failing item:

```csharp
public class CartValidatorTests
{
    private readonly CartValidator _validator = new(new LineItemValidator());

    [Fact]
    public void Invalid_collection_item_uses_index_prefix()
    {
        var cart = new Cart
        {
            CartId = "CART-001",
            Items = new List<LineItem>
            {
                new LineItem { Sku = "SKU-A", Quantity = 1 },
                new LineItem { Sku = "",      Quantity = 0 }
            }
        };

        var result = _validator.Validate(cart);
        ValidationAssert.HasError(result, "Items[1].Sku");
        ValidationAssert.HasError(result, "Items[1].Quantity");
    }
}
```

## Testing without ValidationAssert

You can also assert directly on `result.Failures` using your preferred framework:

```csharp
var result = _validator.Validate(request);

Assert.False(result.IsValid);
Assert.Contains(result.Failures.ToArray(), f => f.PropertyName == "Email");
```

`result.Failures` is `ReadOnlySpan<ValidationFailure>`. Call `.ToArray()` or iterate with `foreach (ref readonly var f in result.Failures)` if your assertion library does not support spans directly.
