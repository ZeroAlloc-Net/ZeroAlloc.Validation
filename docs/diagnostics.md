---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZV0011–ZV0015 Roslyn analyzer rules emitted by ZeroAlloc.Validation.Generator, with triggers, severities, and fix guidance.
sidebar_position: 11
---

# Compiler Diagnostics

ZeroAlloc.Validation.Generator emits the following Roslyn diagnostics at compile time.

| ID | Severity | Title |
|---|---|---|
| [ZV0011](#zv0011) | Warning | Redundant [ValidateWith] attribute |
| [ZV0012](#zv0012) | Error | Invalid [ValidateWith] validator type |
| [ZV0013](#zv0013) | Error | Invalid [CustomValidation] method signature |
| [ZV0015](#zv0015) | Error | Duplicate pipeline behavior Order |

> **Note:** ZV0014 is reserved for a planned future diagnostic ("async-only behavior used with sync-only call site") and is not yet emitted.

---

## ZV0011

**Severity:** Warning

**Title:** Redundant [ValidateWith] attribute

**When fired:** `[ValidateWith]` is applied to a property whose type already carries `[Validate]`. The auto-generated validator is used by default — `[ValidateWith]` is only needed for types you do not control.

**Fix:** Remove `[ValidateWith]` from the property and rely on the auto-generated validator, or keep it only if you need to override the default with a custom implementation.

---

## ZV0012

**Severity:** Error

**Title:** Invalid [ValidateWith] validator type

**When fired:** The type argument passed to `[ValidateWith(typeof(T))]` does not implement `ValidatorFor<TProperty>` for the property type.

**Fix:** Replace the type argument with a class that extends `ValidatorFor<TProperty>`, where `TProperty` matches the type of the annotated property.

---

## ZV0013

**Severity:** Error

**Title:** Invalid [CustomValidation] method signature

**When fired:** A method decorated with `[CustomValidation]` has parameters, or does not return `IEnumerable<ValidationFailure>`.

**Fix:** Ensure the method has no parameters and returns `IEnumerable<ValidationFailure>`:

```csharp
[CustomValidation]
public IEnumerable<ValidationFailure> ValidateBusinessRules()
{
    // yield return failures as needed
}
```

---

## ZV0015

**Severity:** Error

**Title:** Duplicate pipeline behavior Order

**When fired:** Two `[PipelineBehavior]` classes targeting the same model have the same `Order` value. The execution order of the behavior chain would be ambiguous.

**Fix:** Assign a unique `Order` value to each behavior:

```csharp
[PipelineBehavior(Order = 0)]
public class LoggingBehavior : IPipelineBehavior { /* ... */ }

[PipelineBehavior(Order = 1)]   // was also 0 — now unique
public class AuditBehavior : IPipelineBehavior { /* ... */ }
```
