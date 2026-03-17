---
id: error-messages
title: Error Messages
slug: /docs/error-messages
description: Default error message format, [DisplayName] overrides, and the ValidationFailure structure.
sidebar_position: 6
---

# Error Messages

## ValidationFailure structure

Every rule violation is represented as a `ValidationFailure` — a `readonly struct` with four `init`-only properties:

```csharp
public readonly struct ValidationFailure
{
    public string    PropertyName { get; init; }
    public string    ErrorMessage { get; init; }
    public string?   ErrorCode    { get; init; }
    public Severity  Severity     { get; init; }
}
```

| Property | Type | Description |
|---|---|---|
| `PropertyName` | `string` | The property path — e.g., `"Email"`, `"ShippingAddress.Street"`, `"Items[0].Sku"` |
| `ErrorMessage` | `string` | The human-readable error message |
| `ErrorCode` | `string?` | Optional machine-readable code; `null` if not set |
| `Severity` | `Severity` | `Error` (default), `Warning`, or `Info` |

## Severity enum

```csharp
public enum Severity { Error, Warning, Info }
```

`Error` is the zero value and therefore the default. The source generator omits the `Severity` property from the failure initializer when the severity is `Error`, relying on the struct's default zero value.

To filter failures by severity after validation:

```csharp
var result = validator.Validate(model);

// Only hard errors
foreach (ref readonly var f in result.Failures)
{
    if (f.Severity == Severity.Error)
        Console.WriteLine($"Error: {f.PropertyName}: {f.ErrorMessage}");
}

// Collect warnings separately
foreach (ref readonly var f in result.Failures)
{
    if (f.Severity == Severity.Warning)
        Console.WriteLine($"Warning: {f.PropertyName}: {f.ErrorMessage}");
}
```

## Default message format

Default messages use the property name (or display name, see below) directly without quotes.

| Attribute | Default message |
|---|---|
| `[NotNull]` | `Email must not be null.` |
| `[NotEmpty]` | `Email must not be empty.` |
| `[MinLength(5)]` | `Email must be at least 5 characters.` |
| `[MaxLength(50)]` | `Email must not exceed 50 characters.` |
| `[GreaterThan(0)]` | `Amount must be greater than 0.` |
| `[LessThan(100)]` | `Amount must be less than 100.` |
| `[EmailAddress]` | `Email must be a valid email address.` |
| `[Matches(@"^\d+$")]` | `Email does not match the required pattern.` |
| `[Must(nameof(IsValid))]` | `Email is invalid.` |

## Overriding the message

Every attribute that inherits from `ValidationAttribute` exposes a `Message` property. The value you provide is embedded directly into the generated code as a string literal — there is no runtime format evaluation.

```csharp
[NotEmpty(Message = "Email address is required.")]
public string Email { get; set; } = "";
```

### {value} placeholder

Custom `Message` strings may contain the `{value}` placeholder. The generator replaces it with the actual property value at runtime:

```csharp
[MaxLength(10, Message = "'{value}' is too long — maximum 10 characters.")]
public string Code { get; set; } = "";
```

When `Code` is `"TOOLONGVALUE"`, the failure message will read:

```
'TOOLONGVALUE' is too long — maximum 10 characters.
```

## [DisplayName] — overriding the property label

`[DisplayName("display name")]` changes the label used in all default messages for that property. It does not affect `PropertyName` in the failure — that always reflects the actual C# property name.

```csharp
[DisplayName("Email Address")]
[NotEmpty]
[EmailAddress]
public string Email { get; set; } = "";
```

The failures produced will have:

- `ErrorMessage`: `"Email Address must not be empty."` (not `"Email must not be empty."`)
- `ErrorMessage`: `"Email Address must be a valid email address."`
- `PropertyName`: `"Email"` in both cases

`[DisplayName]` is not a `ValidationAttribute`; it applies only to the message label and has no `Message`, `ErrorCode`, or `Severity` properties of its own.

## ErrorCode

Use `ErrorCode` to attach a machine-readable identifier to a failure, useful for API responses or client-side handling:

```csharp
[NotEmpty(ErrorCode = "EMAIL_REQUIRED")]
public string Email { get; set; } = "";

[EmailAddress(ErrorCode = "EMAIL_INVALID_FORMAT")]
public string Email { get; set; } = "";
```

Access it via `f.ErrorCode`. The value is `null` when not set.

## Severity on validation attributes

Set `Severity` on any attribute to mark a rule as a warning or informational notice:

```csharp
[MaxLength(500, Severity = Severity.Warning, Message = "Bio is quite long — consider trimming.")]
public string Bio { get; set; } = "";
```

The failure is still added to `result.Failures`. A non-`Error` severity does **not** prevent `result.IsValid` from being `false` — `IsValid` is `true` only when there are zero failures, regardless of severity.

## Notes on [CustomValidation] methods

Methods decorated with `[CustomValidation]` do not inherit from `ValidationAttribute`, so they do not expose `Message`, `ErrorCode`, or `Severity` as attribute properties. When constructing a `ValidationFailure` inside a custom method, set those fields manually:

```csharp
private static ValidationFailure? ValidateCode(string code)
{
    if (code.Length > 10)
        return new ValidationFailure
        {
            PropertyName = nameof(Code),
            ErrorMessage  = "Code is too long.",
            ErrorCode     = "CODE_TOO_LONG",
            Severity      = Severity.Error,
        };

    return null;
}
```
