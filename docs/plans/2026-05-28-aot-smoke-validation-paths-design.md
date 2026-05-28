# aot-smoke Coverage Extension — Design

**Date:** 2026-05-28
**Scope:** ZeroAlloc.Validation aot-smoke project (`samples/ZeroAlloc.Validation.AotSmoke/`) — extend coverage from the single `[Validate] Address` with `[NotEmpty]` baseline to also exercise three previously-uncovered emission paths: `[ValidateWith(typeof(...))]`, nested `IReadOnlyList<[Validate] T>`, and cross-property `[Must(nameof(Method))]` predicates. Closes backlog item B4.

## Background

The existing aot-smoke project covers exactly one path: a `[Validate]` class with two `[NotEmpty]` properties. It asserts that empty input produces 2 failures and populated input produces 0. That validates the simplest generator-output shape.

It does NOT touch three more-interesting paths the generator emits:

- `[ValidateWith(typeof(MyCustomValidator))]` — points a property at an external `ValidatorFor<T>` subclass written by the user. The generator emits a call into that validator but does not validate its existence at compile time beyond a type check.
- Nested `IReadOnlyList<[Validate] T>` — a `[Validate]` type containing a list of items that are themselves `[Validate]`-decorated. The generator emits a foreach loop with per-item validation and indexed PropertyName reporting (`Items[N].Field`).
- Cross-property `[Must(nameof(Method))]` — a property-level predicate that runs an instance method on the model. Because the method is an instance method (`this.OtherProperty` is reachable), `[Must]` is the canonical cross-property check.

Surfaced 2026-05-28 during the org-wide aot-smoke coverage survey after [ZeroAlloc.Serialisation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation) shipped 2.3.1 + 2.3.2 reactively because its existing smoke only covered the V0 path and left the V1 `[ValueObject]` paths un-validated. Same "smoke exists but partial" pattern applies here — the next downstream consumer to use any of these three Validation paths under Native AOT will discover the regression instead of CI doing so.

## Goal

A regression in the generator-emitted code for any of the three paths fails the aot-smoke job locally. The existing `Address` happy/fail path stays green; the three new fixtures + assertions are strictly additive.

## Decisions

### D-1: three independent fixtures, one per uncovered path

`Letter` (ValidateWith), `Order` (nested), `DateRange` (Must). Each in its own file (`Letter.cs`, `Order.cs`, `DateRange.cs`). Smoke assertions are organised by fixture so a failure pinpoints which feature broke.

**Considered and rejected:**

- **Single combined fixture** (`BigOrder` with all three patterns in one class). Smaller LOC but assertion failures become ambiguous — a "failure count mismatch" doesn't say which feature regressed.

### D-2: tight assertions — count AND PropertyName

Each invalid-input assertion checks failure count AND that PropertyName matches the expected pattern. The nested fixture especially asserts `Items[1]` appears in the PropertyName, which is the load-bearing invariant that distinguishes "index reporting works" from "per-item validation runs but loses the index".

**Considered and rejected:**

- **Count-only assertions** (match existing `Address` style). Looser, more tolerant of PropertyName-format changes, but leaves coverage holes — a regression that drops the index from `Items[1].Field` to `Items.Field` would still pass.

### D-3: no in-process integration test additions

The existing `tests/ZeroAlloc.Validation.Tests/` suite already exercises these three paths in JIT. The smoke project is the AOT-specific regression net. Adding a parallel in-process test would be redundant.

### D-4: no library API changes, no NuGet release

This is purely CI hygiene. No `src/` files touched. The PR ships smoke project changes + a backlog strikethrough. release-please sees `chore:` and skips the release manifest — no NuGet bump. The win is "the next migration in ZA.Validation territory can't silently regress these three paths."

### D-5: strikethrough form for B4 entry

The B4 entry on `main` (added by [PR #49](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/pull/49)) gets struck through to its shipped form in the same PR that ships the work. Mirrors the V1.5/V1.6 strikethrough convention from ZA.Serialisation.

## Design

### Fixture 1 — `[ValidateWith]` pointing at an external validator

**File (NEW):** `samples/ZeroAlloc.Validation.AotSmoke/Letter.cs`

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

// External (non-[Validate]) type — the generator can't emit a validator
// for it directly. [ValidateWith] points at a hand-written ValidatorFor<T>.
public sealed class Postcode
{
    public string Value { get; set; } = "";
}

public sealed class PostcodeValidator : ValidatorFor<Postcode>
{
    public override ValidationResult Validate(Postcode value, ...)
    {
        // Fail when empty; pass otherwise.
        // Concrete signature confirmed in implementation phase against the
        // ValidatorFor<T> base — likely uses a ValidationResult builder
        // or a Failures list parameter.
    }
}

[Validate]
public sealed class Letter
{
    [ValidateWith(typeof(PostcodeValidator))]
    public Postcode Postcode { get; set; } = new();
}
```

The exact `ValidatorFor<T>.Validate` override signature is verified during the implementation Phase 1 (read the base class). Likely `ValidationResult Validate(T value)` or `void Validate(T value, ValidationContext context)`.

**Program.cs assertions:**

- Invalid: `new Letter { Postcode = new() }` (empty Postcode.Value) → 1 failure
- Valid: `new Letter { Postcode = new() { Value = "1234 AB" } }` → 0 failures

### Fixture 2 — Nested `IReadOnlyList<[Validate] T>` with index reporting

**File (NEW):** `samples/ZeroAlloc.Validation.AotSmoke/Order.cs`

```csharp
using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

[Validate]
public sealed class OrderItem
{
    [NotEmpty] public string Sku { get; set; } = "";
}

[Validate]
public sealed class Order
{
    [NotEmpty] public string CustomerName { get; set; } = "";
    public IReadOnlyList<OrderItem> Items { get; set; } = System.Array.Empty<OrderItem>();
}
```

**Program.cs assertions:**

- Invalid: Order with valid CustomerName + Items list `[validItem, badItem]` (where badItem has empty Sku) → 1 failure, PropertyName contains `Items[1]`
- Valid: Order with valid CustomerName + Items list `[validItem]` → 0 failures
- Optional: empty Items list `[]` → 0 failures (or 1 if the generator validates non-emptiness — verify; the design accepts whatever the generator's documented semantics are)

### Fixture 3 — Cross-property `[Must]`

**File (NEW):** `samples/ZeroAlloc.Validation.AotSmoke/DateRange.cs`

```csharp
using System;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

[Validate]
public sealed class DateRange
{
    public DateTime StartDate { get; set; }

    [Must(nameof(IsAfterStart))]
    public DateTime EndDate { get; set; }

    // Instance method — sees `this.StartDate` for the cross-property check.
    // Signature per docs/attributes.md: bool MethodName(TPropType value).
    public bool IsAfterStart(DateTime endValue) => endValue > StartDate;
}
```

**Program.cs assertions:**

- Invalid: `new DateRange { StartDate = T+1, EndDate = T }` → 1 failure, PropertyName is `EndDate`
- Valid: `new DateRange { StartDate = T, EndDate = T+1 }` → 0 failures

### Program.cs structure

After the existing `Address` happy/fail block (lines 1-33 of current file), append three additional blocks following the same pattern: validator construction → invalid input → assertion → valid input → assertion. Each block exits non-zero on failure.

Total Program.cs growth: ~70 LOC (3 blocks × ~20-25 LOC each).

### Backlog update

Find the existing B4 entry in `docs/backlog.md` and replace its content with a struck-through shipped marker:

```markdown
## ~~B4 — Extend aot-smoke to cover `[ValidateWith]`, nested chains, cross-property `[Must]` predicates~~ — ✅ shipped 2026-05-28

**Shipped:** Three new fixtures in `samples/ZeroAlloc.Validation.AotSmoke/` (`Letter.cs`, `Order.cs`, `DateRange.cs`) plus matching assertion blocks in `Program.cs` exercise the three previously-uncovered generator-emission paths. Asserts both invalid + valid input, both failure count + PropertyName patterns — including the load-bearing `Items[1]` indexed PropertyName for the nested-collection case.

**Design + plan:** [`docs/plans/2026-05-28-aot-smoke-validation-paths-design.md`](plans/2026-05-28-aot-smoke-validation-paths-design.md) + [`docs/plans/2026-05-28-aot-smoke-validation-paths.md`](plans/2026-05-28-aot-smoke-validation-paths.md).
```

### Files touched

- **NEW:** `samples/ZeroAlloc.Validation.AotSmoke/Letter.cs`
- **NEW:** `samples/ZeroAlloc.Validation.AotSmoke/Order.cs`
- **NEW:** `samples/ZeroAlloc.Validation.AotSmoke/DateRange.cs`
- **MOD:** `samples/ZeroAlloc.Validation.AotSmoke/Program.cs` — three additional assertion blocks
- **MOD:** `docs/backlog.md` — strike B4 shipped

Total commit footprint: ~110 LOC.

## Out of scope

- **Empty-list semantics test** — if the generator's nested-validation produces 0 failures for an empty list (the natural default), the smoke happens to validate that as a side-effect of the "valid Order" assertion. Not a separately-asserted invariant.
- **Combinations of features** — a fixture exercising e.g. ValidateWith + Must + nesting simultaneously. YAGNI; each fixture covers one feature.
- **Backlog items in ZA.Inject and ZA.AsyncEvents** — separate workstreams, separate PRs.
