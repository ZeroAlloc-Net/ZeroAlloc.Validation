# Design: Enriched Failure Metadata + Cascade Stop

**Date:** 2026-03-16
**Status:** Approved

---

## Goals

1. Allow every validation attribute to carry `ErrorCode` and `Severity` named params that propagate into `ValidationFailure`.
2. Allow a property to declare `[StopOnFirstFailure]` so the generator emits early-exit logic after the first failing rule.

Both features are purely additive — no breaking changes, no new types.

---

## Section 1 — Enriched Failure Metadata

### What exists

`ValidationFailure` already has:

```csharp
public string? ErrorCode { get; init; }
public Severity Severity { get; init; }
```

`Severity` is already defined as:

```csharp
public enum Severity { Error, Warning, Info }
```

The generator currently emits `ValidationFailure` with only `PropertyName` and `ErrorMessage` set.

### Change

Every validation attribute gains two optional named parameters:

| Parameter | Type | Default |
|-----------|------|---------|
| `ErrorCode` | `string?` | `null` |
| `Severity` | `Severity` | `Severity.Error` |

Example:

```csharp
[NotEmpty(ErrorCode = "NAME_REQUIRED")]
[MaximumLength(100, Severity = Severity.Warning, Message = "Name is long but allowed.")]
public string Name { get; set; }
```

### Generator change

The generator reads `ErrorCode` and `Severity` from each attribute and emits them into the `ValidationFailure` initializer:

```csharp
buffer[count++] = new global::ZValidation.ValidationFailure
{
    PropertyName = "Name",
    ErrorMessage = "...",
    ErrorCode = "NAME_REQUIRED",       // omitted when null
    Severity = global::ZValidation.Severity.Error,  // omitted when Error (default)
};
```

To keep generated code clean, `ErrorCode` is omitted from the initializer when `null`, and `Severity` is omitted when `Error` (the default).

### Nested / collection forwarding

Inner validator failures pass through unchanged. The outer validator does not overwrite `ErrorCode` or `Severity` on failures produced by nested validators.

---

## Section 2 — Cascade Stop

### Motivation

When multiple rules target the same property, it is often desirable to stop after the first failure — e.g. don't report `MinLength` on a value that is already `null`.

### New attribute

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class StopOnFirstFailureAttribute : Attribute { }
```

No parameters. `Continue` (the default) is expressed by the absence of the attribute.

### Usage

```csharp
[StopOnFirstFailure]
[NotNull]
[MinLength(2)]
[MaximumLength(100)]
public string Name { get; set; }
```

### Generator change

When `[StopOnFirstFailure]` is present on a property, the generator wraps each rule check with an early-exit guard using a captured `count` baseline and a `goto`:

```csharp
var nameStart = count;
// NotNull
if (model.Name is null)
    buffer[count++] = new ValidationFailure { PropertyName = "Name", ErrorMessage = "..." };
if (count > nameStart) goto nameEnd;
// MinLength
if (model.Name is not null && model.Name.Length < 2)
    buffer[count++] = new ValidationFailure { PropertyName = "Name", ErrorMessage = "..." };
if (count > nameStart) goto nameEnd;
// MaximumLength
if (model.Name is not null && model.Name.Length > 100)
    buffer[count++] = new ValidationFailure { PropertyName = "Name", ErrorMessage = "..." };
nameEnd:;
```

`goto` avoids heap allocations and nested `if` trees. Zero-alloc compliant.

### Edge cases

- **`When`/`Unless` conditions**: the `if (count > nameStart) goto nameEnd;` guard is placed *inside* the condition block so cascade only applies when the condition is active.
- **Nested/collection validators**: `[StopOnFirstFailure]` has no effect on nested validator instances — those manage their own cascade behavior.
- **Zero rules on property**: no-op; generator emits nothing extra.

---

## Section 3 — Testing

### Generator emission tests

- `ErrorCode` appears in the emitted `ValidationFailure` initializer when set.
- `Severity` appears in the emitted initializer when not `Error`.
- Both are omitted from the initializer when at defaults.
- `[StopOnFirstFailure]` causes `goto` / early-exit pattern to appear in emitted code.

### Integration tests

- `result.Failures[0].ErrorCode == "NAME_REQUIRED"` when `[NotEmpty(ErrorCode = "NAME_REQUIRED")]`.
- `result.Failures[0].Severity == Severity.Warning` when `[MaximumLength(100, Severity = Severity.Warning)]`.
- `[StopOnFirstFailure]` + null value → exactly one failure (not two), even though `MinLength` would also fail.
- `[StopOnFirstFailure]` + `When` → cascade only triggers when condition is active.
- Nested validator failures preserve inner `ErrorCode`/`Severity` unchanged.

### No new analyzer diagnostics

Both features are purely additive named params / marker attributes with no correctness constraints to enforce at compile time.

---

## Out of Scope

- Class-level / global cascade mode (future milestone).
- Per-rule default severity (all rules default to `Error`; explicit override only).
- `WithErrorCode` / `WithSeverity` fluent chaining (no fluent API in this library).
