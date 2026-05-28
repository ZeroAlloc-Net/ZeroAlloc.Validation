# aot-smoke Validation Paths Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extend `samples/ZeroAlloc.Validation.AotSmoke/` to cover three previously-uncovered generator-emission paths under `PublishAot=true`: `[ValidateWith(typeof(...))]`, nested `IReadOnlyList<[Validate] T>` with indexed PropertyName, and cross-property `[Must(nameof(Method))]` predicates. Closes backlog item B4.

**Architecture:** Three new independent fixture files (`Letter.cs`, `Order.cs`, `DateRange.cs`), each focused on one path. Three new assertion blocks appended to `Program.cs` — each block exercises invalid + valid input and asserts failure count AND PropertyName patterns (including the load-bearing `Items[1]` indexed PropertyName for the nested case). No library changes; smoke + backlog strikethrough only.

**Tech Stack:** .NET 10, `PublishAot=true`, BenchmarkDotNet not involved (this is a runtime correctness smoke, not a perf smoke).

**Design doc:** `docs/plans/2026-05-28-aot-smoke-validation-paths-design.md` (committed at `25ca4ee`).

**Working branch:** `chore/aot-smoke-cover-validation-paths` (off `main` at `17a6dbb`; design committed).

---

## Phase 0 — Orient (5 min)

### Task 0.1: Read the existing smoke + Validation library APIs

**Files (read-only):**

- `samples/ZeroAlloc.Validation.AotSmoke/Program.cs` — current shape; new blocks follow this assertion idiom (`if (failed) { Error.WriteLine; return 1; }`).
- `samples/ZeroAlloc.Validation.AotSmoke/Address.cs` — current `[Validate]` fixture; new fixtures follow this file structure (one type per file, `ZeroAlloc.Validation.AotSmoke` namespace).
- `src/ZeroAlloc.Validation/Core/ValidatorFor.cs` — confirms the signature for fixture 1's `PostcodeValidator`: override `public abstract ValidationResult Validate(T instance);`.
- `src/ZeroAlloc.Validation/Core/ValidationResult.cs` — `IsValid` + `Failures` (`ReadOnlySpan<ValidationFailure>`).
- `src/ZeroAlloc.Validation/Core/ValidationFailure.cs` (or grep for the struct) — has `PropertyName`, `ErrorMessage`, `ErrorCode`, `Severity`.
- `src/ZeroAlloc.Validation/Attributes/ValidateWithAttribute.cs` — confirms `[ValidateWith(typeof(...))]` on properties.
- `src/ZeroAlloc.Validation/Attributes/MustAttribute.cs` — confirms `[Must(string methodName)]` on properties.
- `docs/attributes.md` lines around `[Must]` — confirms the method signature: instance method `bool MethodName(TPropType value)` on the model.

---

## Phase 1 — Fixture 1: `[ValidateWith]` (35 min, 5 tasks)

### Task 1.1: Write `Letter.cs`

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

// User-written ValidatorFor<Postcode> — exercises the [ValidateWith] path
// end-to-end. The generator emits a call into this validator at the
// [ValidateWith]-annotated property's evaluation site.
public sealed class PostcodeValidator : ValidatorFor<Postcode>
{
    public override ValidationResult Validate(Postcode instance)
    {
        if (string.IsNullOrEmpty(instance.Value))
        {
            return new ValidationResult(new[]
            {
                new ValidationFailure
                {
                    PropertyName = nameof(Postcode.Value),
                    ErrorMessage = "Postcode is required.",
                    ErrorCode = null,
                    Severity = Severity.Error,
                },
            });
        }
        return new ValidationResult(System.Array.Empty<ValidationFailure>());
    }
}

[Validate]
public sealed class Letter
{
    [ValidateWith(typeof(PostcodeValidator))]
    public Postcode Postcode { get; set; } = new();
}
```

If `Severity.Error` isn't the canonical name (e.g. `Severity.Critical` or some other naming), confirm against the actual enum and adjust. Same for the `ValidationResult` constructor signature if it differs from `(ValidationFailure[])`.

### Task 1.2: Append the assertion block to `Program.cs`

**File (MOD):** `samples/ZeroAlloc.Validation.AotSmoke/Program.cs`

After the existing `Address` block (current line 33, after `return 0;` becomes a marker — actually `return 0;` is the FINAL line; assertion blocks go BEFORE it).

Read the file, find the final `Console.WriteLine("AOT smoke: PASS");` line, and append before it:

```csharp
// Fixture 1: [ValidateWith] pointing at an external ValidatorFor<T>.
// Generator emits a call into PostcodeValidator at the [ValidateWith]
// site. Failure flows back into the parent Letter's failures.
var letterValidator = new LetterValidator();

// Invalid: empty Postcode.Value → 1 failure routed through PostcodeValidator.
var emptyLetter = letterValidator.Validate(new Letter { Postcode = new() });
if (emptyLetter.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — Letter with empty Postcode should be invalid");
    return 1;
}

var emptyLetterFailures = System.Linq.Enumerable.ToArray(System.MemoryExtensions.ToArray(emptyLetter.Failures));
// NOTE: ReadOnlySpan can't be passed to LINQ directly; use index/length iteration.
// Rewrite the count + pattern check inline:
int letterFailureCount = 0;
bool letterHasPostcodePropertyName = false;
foreach (ref readonly var f in emptyLetter.Failures)
{
    letterFailureCount++;
    if (f.PropertyName.Contains("Postcode", System.StringComparison.Ordinal))
        letterHasPostcodePropertyName = true;
}
if (letterFailureCount != 1 || !letterHasPostcodePropertyName)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — Letter expected 1 failure with Postcode in PropertyName, got {letterFailureCount} failures (PostcodeMatch={letterHasPostcodePropertyName})");
    return 1;
}

// Valid: populated Postcode.Value → 0 failures.
var validLetter = letterValidator.Validate(new Letter { Postcode = new() { Value = "1234 AB" } });
if (!validLetter.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — Letter with populated Postcode should be valid");
    return 1;
}
```

The `ReadOnlySpan<ValidationFailure>` iteration uses `foreach ref readonly var` per the existing patterns in `tests/ZeroAlloc.Validation.Tests/`. If the test suite uses a different convention (e.g. `Failures.ToArray()` via a helper), match that.

The exact `letterValidator` class name (`LetterValidator`) is the generator's emission convention — confirm by inspecting an existing `*Validator` reference in either the smoke or the tests.

### Task 1.3: Build the smoke project

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet build samples/ZeroAlloc.Validation.AotSmoke/ZeroAlloc.Validation.AotSmoke.csproj -c Release 2>&1 | tail -20
```

Expected: build succeeds. If it fails:
- **Generator can't find `PostcodeValidator`** — the `[ValidateWith(typeof(PostcodeValidator))]` reference must compile. Check namespace + visibility.
- **`Severity.Error` not the enum value** — adjust to actual member.
- **`ValidationResult` constructor signature mismatch** — adjust to actual.

If the build succeeds but warnings about IL2026/IL3050 surface, they'll be promoted to errors per the csproj's `<WarningsAsErrors>`. Investigate and fix before continuing.

### Task 1.4: Local smoke run (best-effort under Windows AOT toolchain)

```bash
dotnet publish samples/ZeroAlloc.Validation.AotSmoke/ZeroAlloc.Validation.AotSmoke.csproj -c Release -o ./aot-out 2>&1 | tail -10
./aot-out/ZeroAlloc.Validation.AotSmoke
```

Expected: `AOT smoke: PASS` + exit 0. If native link fails on Windows (no clang, no link.exe in PATH, etc.), skip this step — CI will run it on ubuntu-latest with clang+zlib installed.

If the build step passed but a `dotnet run -c Release` (no publish) fails the assertion, that's a meaningful failure — investigate before committing.

### Task 1.5: Commit Fixture 1

```bash
git add samples/ZeroAlloc.Validation.AotSmoke/Letter.cs \
        samples/ZeroAlloc.Validation.AotSmoke/Program.cs
git commit -m "chore(aot-smoke): cover [ValidateWith] via PostcodeValidator fixture

External (non-[Validate]) Postcode type with a hand-written
ValidatorFor<Postcode>. [ValidateWith(typeof(PostcodeValidator))] on
the Letter.Postcode property exercises the generator's external-validator
emission path. Asserts both failure count (1) and PropertyName pattern
(contains 'Postcode') under PublishAot=true."
```

---

## Phase 2 — Fixture 2: Nested `IReadOnlyList<[Validate] T>` (35 min, 5 tasks)

### Task 2.1: Write `Order.cs`

**File (NEW):** `samples/ZeroAlloc.Validation.AotSmoke/Order.cs`

```csharp
using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

[Validate]
public sealed class OrderItem
{
    [NotEmpty(Message = "Sku is required.")]
    public string Sku { get; set; } = "";
}

[Validate]
public sealed class Order
{
    [NotEmpty] public string CustomerName { get; set; } = "";
    public IReadOnlyList<OrderItem> Items { get; set; } = System.Array.Empty<OrderItem>();
}
```

The generator detects `OrderItem` is `[Validate]`-decorated and emits a `foreach` in `OrderValidator.Validate` that runs `new OrderItemValidator().Validate(item)` per element, with PropertyName prefixed `Items[N].` per documented behavior. The 1.4.1 fix (B3) ensured the null-guard around the loop element is correctly omitted for value-type elements — `OrderItem` is a class here, not a struct, so the guard fires (a regression-net test for that case would be a separate smoke addition; not in scope for B4).

### Task 2.2: Append the nested assertion block to Program.cs

**File (MOD):** `samples/ZeroAlloc.Validation.AotSmoke/Program.cs`

Append before the final `Console.WriteLine("AOT smoke: PASS");`:

```csharp
// Fixture 2: Nested IReadOnlyList<[Validate] OrderItem> with per-item indexing.
// Generator emits a foreach over Items, validating each via OrderItemValidator
// and emitting failures with PropertyName "Items[N].Sku" — the indexed
// PropertyName is the load-bearing invariant.
var orderValidator = new OrderValidator();

// Invalid: one valid item at [0], one invalid item at [1].
var mixedOrder = orderValidator.Validate(new Order
{
    CustomerName = "Alice",
    Items = new[]
    {
        new OrderItem { Sku = "SKU-1" },
        new OrderItem { Sku = "" }, // index 1 — invalid
    },
});
if (mixedOrder.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — Order with one invalid item should be invalid");
    return 1;
}

int mixedFailureCount = 0;
bool mixedHasIndexedPropertyName = false;
foreach (ref readonly var f in mixedOrder.Failures)
{
    mixedFailureCount++;
    if (f.PropertyName.Contains("Items[1]", System.StringComparison.Ordinal))
        mixedHasIndexedPropertyName = true;
}
if (mixedFailureCount != 1 || !mixedHasIndexedPropertyName)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — Order expected 1 failure with 'Items[1]' in PropertyName, got {mixedFailureCount} failures (IndexedMatch={mixedHasIndexedPropertyName})");
    foreach (ref readonly var f in mixedOrder.Failures)
        Console.Error.WriteLine($"  failure: PropertyName='{f.PropertyName}', ErrorMessage='{f.ErrorMessage}'");
    return 1;
}

// Valid: all items valid + valid CustomerName.
var validOrder = orderValidator.Validate(new Order
{
    CustomerName = "Alice",
    Items = new[] { new OrderItem { Sku = "SKU-1" } },
});
if (!validOrder.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — fully-valid Order should be valid");
    return 1;
}
```

If the generator's actual PropertyName format differs from `Items[1].Sku` (e.g. `[1].Sku` or `Items[1]`), adjust the substring assertion to whatever the generator emits. The first run will reveal it via the diagnostic output of the failure case.

### Task 2.3: Build + run

```bash
dotnet build samples/ZeroAlloc.Validation.AotSmoke/ZeroAlloc.Validation.AotSmoke.csproj -c Release 2>&1 | tail -10
```

Expected: build succeeds.

If the assertion fails at runtime because PropertyName format differs, the diagnostic `Console.Error.WriteLine` block above prints the actual `PropertyName`. Use that to adjust the substring.

### Task 2.4: Commit Fixture 2

```bash
git add samples/ZeroAlloc.Validation.AotSmoke/Order.cs \
        samples/ZeroAlloc.Validation.AotSmoke/Program.cs
git commit -m "chore(aot-smoke): cover nested IReadOnlyList<[Validate]> with indexed PropertyName

Order with IReadOnlyList<OrderItem> exercises the generator's per-element
foreach + indexed PropertyName emission. Asserts the load-bearing
'Items[1]' substring in PropertyName for a failure at the second element —
catches a regression that loses the index reporting."
```

### Task 2.5: Confirm the existing Address smoke still passes

```bash
dotnet build samples/ZeroAlloc.Validation.AotSmoke/ZeroAlloc.Validation.AotSmoke.csproj -c Release
# If a local AOT toolchain is available, also:
dotnet publish samples/ZeroAlloc.Validation.AotSmoke/ZeroAlloc.Validation.AotSmoke.csproj -c Release -o ./aot-out && ./aot-out/ZeroAlloc.Validation.AotSmoke
```

Expected: `AOT smoke: PASS`. The existing Address block + new Letter block + new Order block all green.

---

## Phase 3 — Fixture 3: Cross-property `[Must]` (25 min, 4 tasks)

### Task 3.1: Write `DateRange.cs`

**File (NEW):** `samples/ZeroAlloc.Validation.AotSmoke/DateRange.cs`

```csharp
using System;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

[Validate]
public sealed class DateRange
{
    public DateTime StartDate { get; set; }

    [Must(nameof(IsAfterStart), Message = "EndDate must be after StartDate.")]
    public DateTime EndDate { get; set; }

    // Instance method — generator emits a direct call (no reflection).
    // The body sees `this.StartDate` for the cross-property comparison.
    public bool IsAfterStart(DateTime endValue) => endValue > StartDate;
}
```

If `MustAttribute` doesn't accept a `Message` property (the snippet above is the docs' canonical form — verify against the actual `MustAttribute` source from Phase 0), drop the `Message = "..."` part and rely on whatever default message the generator emits.

### Task 3.2: Append the [Must] assertion block to Program.cs

**File (MOD):** `samples/ZeroAlloc.Validation.AotSmoke/Program.cs`

Append before the final `Console.WriteLine("AOT smoke: PASS");`:

```csharp
// Fixture 3: Cross-property [Must] predicate.
// Generator emits a direct call to IsAfterStart(this.EndDate) on the model.
// The method accesses this.StartDate for the cross-property comparison.
var dateRangeValidator = new DateRangeValidator();

var anchor = new DateTime(2026, 5, 28);

// Invalid: EndDate before StartDate.
var badRange = dateRangeValidator.Validate(new DateRange
{
    StartDate = anchor.AddDays(1),
    EndDate = anchor,
});
if (badRange.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — DateRange with EndDate < StartDate should be invalid");
    return 1;
}

int badRangeFailureCount = 0;
bool badRangeHasEndDatePropertyName = false;
foreach (ref readonly var f in badRange.Failures)
{
    badRangeFailureCount++;
    if (string.Equals(f.PropertyName, "EndDate", System.StringComparison.Ordinal))
        badRangeHasEndDatePropertyName = true;
}
if (badRangeFailureCount != 1 || !badRangeHasEndDatePropertyName)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — DateRange expected 1 failure with PropertyName='EndDate', got {badRangeFailureCount} failures (EndDateMatch={badRangeHasEndDatePropertyName})");
    foreach (ref readonly var f in badRange.Failures)
        Console.Error.WriteLine($"  failure: PropertyName='{f.PropertyName}', ErrorMessage='{f.ErrorMessage}'");
    return 1;
}

// Valid: EndDate after StartDate.
var goodRange = dateRangeValidator.Validate(new DateRange
{
    StartDate = anchor,
    EndDate = anchor.AddDays(1),
});
if (!goodRange.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — DateRange with EndDate > StartDate should be valid");
    return 1;
}
```

The `EndDate` PropertyName exact-match assertion is tighter than the `Contains` check used in fixtures 1+2 — `[Must]` on a single property should produce exactly that PropertyName. If the generator's actual emission differs (e.g. it appends the method name like `EndDate.IsAfterStart`), adjust to a `Contains` check.

### Task 3.3: Build + run

```bash
dotnet build samples/ZeroAlloc.Validation.AotSmoke/ZeroAlloc.Validation.AotSmoke.csproj -c Release 2>&1 | tail -10
```

Expected: build succeeds.

### Task 3.4: Commit Fixture 3

```bash
git add samples/ZeroAlloc.Validation.AotSmoke/DateRange.cs \
        samples/ZeroAlloc.Validation.AotSmoke/Program.cs
git commit -m "chore(aot-smoke): cover cross-property [Must] predicate

DateRange.[Must(nameof(IsAfterStart))] on EndDate exercises the
generator's cross-property predicate emission. The IsAfterStart
instance method sees this.StartDate via implicit capture. Asserts
PropertyName exact-match 'EndDate' on the failure — catches a
regression in [Must]'s emission shape."
```

---

## Phase 4 — Strike B4 + push + PR (15 min, 3 tasks)

### Task 4.1: Update `docs/backlog.md`

Read the file to find the existing B4 entry (added by PR #49). Replace its content with the struck-through shipped marker:

```markdown
## ~~B4 — Extend aot-smoke to cover `[ValidateWith]`, nested chains, cross-property `[Must]` predicates~~ — ✅ shipped 2026-05-28

**Shipped:** Three new fixtures in `samples/ZeroAlloc.Validation.AotSmoke/` (`Letter.cs`, `Order.cs`, `DateRange.cs`) plus matching assertion blocks in `Program.cs` exercise the three previously-uncovered generator-emission paths. Asserts both invalid + valid input, both failure count + PropertyName patterns — including the load-bearing `Items[1]` indexed PropertyName for the nested-collection case.

**Design + plan:** [`docs/plans/2026-05-28-aot-smoke-validation-paths-design.md`](plans/2026-05-28-aot-smoke-validation-paths-design.md) + [`docs/plans/2026-05-28-aot-smoke-validation-paths.md`](plans/2026-05-28-aot-smoke-validation-paths.md).
```

Replace the existing B4 block (the open "What/Why/Sketch/Tradeoff/Graduation signal" form) with the above. Don't add a new entry — strikethrough in place.

### Task 4.2: Full smoke check + commit docs

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet build -c Release 2>&1 | tail -10
```

Expected: full solution builds. The smoke project compiles; existing test suite still passes (no library changes, but build = all projects).

```bash
git add docs/backlog.md
git commit -m "docs(backlog): strike B4 shipped (aot-smoke coverage extension)"
```

### Task 4.3: Push + open PR + STOP

```bash
git push -u origin chore/aot-smoke-cover-validation-paths

gh pr create \
  --title "chore(aot-smoke): cover ValidateWith, nested, cross-property Must paths" \
  --body "$(cat <<'EOF'
## Summary

Closes backlog item B4. The existing aot-smoke project covered only the simple `[Validate]` + `[NotEmpty]` baseline; this PR adds three independent fixtures + assertion blocks exercising the three previously-uncovered generator-emission paths:

- `[ValidateWith(typeof(PostcodeValidator))]` — external user-written `ValidatorFor<T>` invocation
- Nested `IReadOnlyList<[Validate] OrderItem>` — per-element foreach with indexed `Items[N].Sku` PropertyName
- Cross-property `[Must(nameof(IsAfterStart))]` — instance-method predicate accessing other properties via `this.`

## Why now

Surfaced 2026-05-28 during the org-wide aot-smoke coverage survey done after [ZeroAlloc.Serialisation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation) shipped 2.3.1 + 2.3.2 reactively. ZA.Serialisation's smoke covered only the V0 path; V1 paths were left un-validated and downstream templates discovered the gap. Same "smoke exists but partial" pattern applied to ZA.Validation; this PR closes it.

## What changed

- 3 new fixture files (`Letter.cs`, `Order.cs`, `DateRange.cs`)
- 3 new assertion blocks in `Program.cs` (~70 LOC)
- 1 line in `docs/backlog.md` — B4 entry struck shipped

## Decisions ([design doc](docs/plans/2026-05-28-aot-smoke-validation-paths-design.md))

- **Three independent fixtures, not one combined** — failure messages pinpoint which feature regressed
- **Tight assertions (count AND PropertyName)** — catches index-dropped regressions in nested case, predicate-mistargeted regressions in [Must] case
- **No in-process tests added** — existing `tests/ZeroAlloc.Validation.Tests/` suite already covers JIT; smoke is the AOT-specific net

## SemVer

No package version bump — CI-only changes. release-please will treat as `chore:` and skip the release manifest.

## Test plan

- [x] Local build clean
- [ ] CI build clean
- [ ] CI aot-smoke job passes

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**STOP** after PR opens. Do NOT admin-merge.

---

## Verification checklist

- [ ] **Phase 1:** Letter fixture compiles + assertion fires correctly for empty Postcode.Value
- [ ] **Phase 2:** Order fixture compiles + `Items[1]` substring appears in PropertyName for the indexed failure
- [ ] **Phase 3:** DateRange fixture compiles + `EndDate` exact-match PropertyName on the [Must] failure
- [ ] **Phase 4:** B4 backlog struck through, PR opens cleanly with all three fixtures present

## Out of scope (deferred to backlog or future PRs)

- **Empty-list semantics test** for Order — the "valid Order" assertion happens to validate empty/non-empty list handling as a side-effect
- **Multi-feature combinations** — a fixture exercising e.g. ValidateWith + Must + nesting simultaneously. YAGNI
- **Backlog items in ZA.Inject (#66) and ZA.AsyncEvents (#75)** — separate workstreams, separate PRs
- **B3 regression-net for value-type nested elements** — ZA.Validation 1.4.1 fixed this case; a smoke fixture for `IReadOnlyList<[Validate] readonly record struct>` would be belt-and-suspenders; defer
