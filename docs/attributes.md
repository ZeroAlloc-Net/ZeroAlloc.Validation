---
id: attributes
title: Attribute Reference
slug: /docs/attributes
description: Complete reference for all built-in validation attributes in ZeroAlloc.Validation.
sidebar_position: 2
---

All validation attributes live in the `ZeroAlloc.Validation` namespace and inherit from `ValidationAttribute`. Every attribute exposes these shared properties: `Message` (override default error text), `ErrorCode` (machine-readable code), `Severity` (default `Error`), `When` (method name — run rule only when this returns `true`), `Unless` (method name — run rule only when this returns `false`).

## String attributes

| Attribute | Description | Default error message |
|---|---|---|
| `[NotEmpty]` | Must not be null or empty | `PropertyName must not be empty.` |
| `[Empty]` | Must be null or empty | `PropertyName must be empty.` |
| `[MaxLength(n)]` | Length ≤ n | `PropertyName must not exceed n characters.` |
| `[MinLength(n)]` | Length ≥ n | `PropertyName must be at least n characters.` |
| `[Length(min,max)]` | min ≤ length ≤ max | `PropertyName must be between min and max characters.` |
| `[Matches(pattern)]` | Must match regex pattern | `PropertyName does not match the required pattern.` |
| `[EmailAddress]` | Must be a valid email address | `PropertyName must be a valid email address.` |

```csharp
[Validate]
public class ContactForm
{
    [NotEmpty][MaxLength(100)]         public string Name    { get; set; } = "";
    [NotEmpty][EmailAddress]           public string Email   { get; set; } = "";
    [Matches(@"^\+?[0-9\s\-()]+$")]    public string Phone   { get; set; } = "";
}
```

> **Note:** `[NotEmpty]` checks `string.IsNullOrEmpty()` — it rejects `null` and `""` but accepts whitespace-only strings such as `"   "`.

## Numeric and comparison attributes

| Attribute | Description | Default error message |
|---|---|---|
| `[GreaterThan(value)]` | > value | `PropertyName must be greater than value.` |
| `[GreaterThanOrEqualTo(value)]` | ≥ value | `PropertyName must be greater than or equal to value.` |
| `[LessThan(value)]` | < value | `PropertyName must be less than value.` |
| `[LessThanOrEqualTo(value)]` | ≤ value | `PropertyName must be less than or equal to value.` |
| `[InclusiveBetween(min, max)]` | min ≤ x ≤ max | `PropertyName must be between min and max.` |
| `[ExclusiveBetween(min, max)]` | min < x < max | `PropertyName must be exclusively between min and max.` |
| `[Equal(value)]` | == value | `PropertyName must be equal to value.` |
| `[NotEqual(value)]` | != value | `PropertyName must not be equal to value.` |
| `[PrecisionScale(p, s)]` | At most `p` total digits, `s` after decimal | `PropertyName must not exceed p digits total with s decimal places.` |

```csharp
[Validate]
public class OrderLine
{
    [GreaterThan(0)]                public decimal UnitPrice { get; set; }
    [InclusiveBetween(1, 1000)]     public int     Quantity  { get; set; }
    [PrecisionScale(10, 2)]         public decimal Discount  { get; set; }
}
```

## Null and existence attributes

| Attribute | Description | Default error message |
|---|---|---|
| `[NotNull]` | Must not be null; also triggers nested validation when the property type carries `[Validate]` | `PropertyName must not be null.` |
| `[Null]` | Must be null | `PropertyName must be null.` |

```csharp
[Validate]
public class Order
{
    [NotNull] public Address? ShippingAddress { get; set; }
    [Null]    public string?  DeletedAt       { get; set; }
}
```

## Enum attributes

| Attribute | Description | Default error message |
|---|---|---|
| `[IsEnumName(typeof(TEnum))]` | String property — value must be a defined name in the specified enum type | `PropertyName is not a valid enum name.` |
| `[IsInEnum]` | Enum-typed property — value must be a defined member of its own enum type | `PropertyName is not a valid value.` |

```csharp
[Validate]
public class SetStatusRequest
{
    [IsInEnum]                          public OrderStatus Status   { get; set; }
    [IsEnumName(typeof(OrderStatus))]   public string      StatusId { get; set; } = "";
}
```

## Behaviour modifiers (on properties)

| Attribute | Description |
|---|---|
| `[StopOnFirstFailure]` | Stop checking further rules on this property after the first failure |
| `[ValidateWith(typeof(TValidator))]` | Use a specific validator for a nested object or collection element |

```csharp
[Validate]
public class CreateUser
{
    [StopOnFirstFailure]
    [NotEmpty][MinLength(3)][MaxLength(50)]
    public string Username { get; set; } = "";
}
```

```csharp
// Use an explicit validator for a nested type
[Validate]
public class Order
{
    [NotNull]
    [ValidateWith(typeof(AddressValidator))]
    public ExternalAddress? ShippingAddress { get; set; }
}
```

## Model-level attributes (on class)

| Attribute | Description |
|---|---|
| `[Validate]` | Marks the class for source generation. Set `StopOnFirstFailure = true` to stop after the first failing property across the entire model. |
| `[SkipWhen(nameof(Method))]` | Skip all validation when the named instance method (no parameters, returning `bool`) returns `true` |

## Custom rule attributes

| Attribute | Target | Description |
|---|---|---|
| `[Must(nameof(Method))]` | Property | Run an instance method `bool MethodName(TPropType value)` on the model; fails if it returns `false` |
| `[CustomValidation]` | Method | Mark an instance method `IEnumerable<ValidationFailure> Method()` (no parameters) to run after all property rules |

```csharp
[Validate]
public class PasswordChange
{
    [NotEmpty]
    public string NewPassword { get; set; } = "";

    [NotEmpty]
    [Must(nameof(MatchesNew))]
    public string ConfirmPassword { get; set; } = "";

    // [Must] — called as instance.MatchesNew(value)
    private bool MatchesNew(string value) => value == NewPassword;

    // [CustomValidation] — called as instance.ValidateStrength()
    [CustomValidation]
    public IEnumerable<ValidationFailure> ValidateStrength()
    {
        if (NewPassword.Length < 8)
            yield return new ValidationFailure
            {
                PropertyName = nameof(NewPassword),
                ErrorMessage = "Password must be at least 8 characters."
            };
    }
}
```

## Shared attribute properties

All rule attributes (`[NotEmpty]`, `[GreaterThan]`, etc.) inherit from `ValidationAttribute` and expose:

```csharp
public abstract class ValidationAttribute : Attribute
{
    public string?   Message   { get; set; }  // override default error text
    public string?   ErrorCode { get; set; }  // machine-readable code
    public Severity  Severity  { get; set; }  // Error (default), Warning, Info
    public string?   When      { get; set; }  // run only when named method returns true
    public string?   Unless    { get; set; }  // run only when named method returns false
}
```

```csharp
[GreaterThan(0, Message = "Amount must be positive.", ErrorCode = "AMOUNT_POS", Severity = Severity.Error)]
public decimal Amount { get; set; }
```

> **Note:** `[CustomValidation]` and `[StopOnFirstFailure]` inherit directly from `System.Attribute`, not from `ValidationAttribute`. They do not support the `Message`, `When`, `Unless`, `ErrorCode`, or `Severity` shared properties.
