# Value-Object-Aware Property Validators — Design

**Date:** 2026-05-27
**Scope:** ZeroAlloc.Validation generator recognises properties whose declared type is a single-property `[ZeroAlloc.ValueObjects.ValueObject]` partial struct and rewrites the validator's property-access expression to unwrap through the single underlying member. Built-in operand-taking validators (`[GreaterThan]`, `[LessThan]`, `[InclusiveBetween]`, `[NotEmpty]`, `[Matches]`, `[EmailAddress]`, `[Length]`, etc.) now accept value-object-typed properties directly; predicate validators (`[Must]`, `[CustomValidation]`, `[ValidateWith]`) keep passing the wrapper unchanged. Closes backlog item B1. Ships as **ZeroAlloc.Validation 1.5.0** (additive minor — new shapes compile that previously didn't; existing class/primitive consumers see no behaviour change).

## Background

`za-vertical-slice`'s six `[Validate]` request types ship today as `readonly record struct X([property: GreaterThan(0)] int CustomerId, ...)` — they had to fall back to **raw primitive** properties because `[GreaterThan(0)]` couldn't see through `CustomerId` (a `[ValueObject]` partial struct wrapping `int`). The handler then wraps to `new CustomerId(request.CustomerId)` on the way through. This weakens the request-side type signal exactly where validation should reinforce it: `int CustomerId` in a `PlaceOrderCommand` reads as "any int", not "a customer's id."

Friction surfaced 2026-05-26 building the `za-vertical-slice` template (ZeroAlloc.Templates 0.4.0 → 0.7.x). Same pattern hits every consumer that combines `[Validate]` with `[ValueObject]`-typed identifiers.

The validators today emit code like:

```csharp
if (instance.CustomerId <= 0)
    _buf.Add(new ValidationFailure { ... });
```

When `CustomerId` is `int`, this compiles. When `CustomerId` is a value-object wrapper, `<=` operator isn't defined → CS0019. The fix rewrites the access expression to `instance.CustomerId.Value` (or whatever the single underlying property name is) **before** the validator emission consumes it.

`[ZeroAlloc.ValueObjects.ValueObjectAttribute]` (sealed, defined in `src/ZeroAlloc.ValueObjects/ValueObjectAttribute.cs`) is the canonical marker. ZA.ValueObjects' source generator emits the underlying property and constructor; the property name is determined by the user's declaration, typically `Value` for TypedIds but anything is legal.

## Goal

`[Validate]` request types can carry value-object-typed properties alongside primitives, and all built-in operand-taking validators participate naturally. Existing class/primitive properties see no behaviour change. A clear diagnostic (`ZV0016` Warning) fires when a multi-property value-object (e.g. `Money { Amount, Currency }`) carries a built-in validator — the unwrap is ambiguous; user picks either a single-property value-object or a custom-predicate validator.

## Decisions

### D-1: detection — attribute-based on `[ValueObject]`'s FQN

Detect value-object types by matching `INamedTypeSymbol.GetAttributes()` against the FQN `ZeroAlloc.ValueObjects.ValueObjectAttribute`. Same pattern ZA.Validation already uses for its own `[Validate]` marker (`ValidateAttributeFqn` in `RuleEmitter.cs`). No runtime-assembly reference to ZA.ValueObjects — only the attribute's metadata name matters.

**Why not duck-typing** ("single readable property of the validator's operand type"): too loose. Any user-defined `readonly struct Wrap(int n)` that happens to have a single readable int property would silently participate, surprising adopters who didn't intend the rewrite. Attribute-based is explicit: the rewrite kicks in exactly when the type's author opted into value-object semantics.

**Why not interface-based** (`IValueObject<T>`): the cleanest API but ZA.ValueObjects doesn't currently emit such an interface. Introducing it would require a coordinated change to ZA.ValueObjects' generator + a major bump. Out of scope for B1.

**Adopters who don't reference ZA.ValueObjects** pay nothing — `GetAttributes()` doesn't find the FQN, helper returns `null`, generator emits identical output to today.

### D-2: scope — built-in operand-taking validators, predicate validators carved out

The rewrite applies uniformly to **every** built-in validator that consumes a property access as an operand:

- Comparison: `[GreaterThan]`, `[GreaterThanOrEqualTo]`, `[LessThan]`, `[LessThanOrEqualTo]`, `[Equal]`, `[NotEqual]`
- Range: `[InclusiveBetween]`, `[ExclusiveBetween]`
- String / collection: `[NotEmpty]`, `[Empty]`, `[Length]`, `[MinLength]`, `[MaxLength]`, `[Matches]`, `[EmailAddress]`
- Structural: `[PrecisionScale]` (decimal)
- Enum: `[IsInEnum]`, `[IsEnumName]`

Predicate validators **don't participate** by design:

- `[Must]` — calls a user-provided predicate whose signature picks its own argument type. If the user wrote `bool IsValid(CustomerId c)` they expect the wrapper; if they wrote `bool IsValid(int v)` they wanted the unwrap. The compile-time type-match between the predicate signature and the property's declared type is what dictates this — the generator doesn't override the user's choice.
- `[CustomValidation]` — same: invokes user code with the property as input.
- `[ValidateWith]` — points at a nested `ValidatorFor<T>`, where T's identity drives the match.

Falls out naturally from the implementation (D-4): the rewrite happens at the **property-access expression** the rule-emission methods consume. Predicate validators don't take that expression as an operand — they pass the property value via the user-controlled method dispatch — so they're never touched by the rewrite.

### D-3: multi-property value-objects — fall through + `ZV0016` Warning

Single-property value-object → unwrap unambiguous → rewrite. Multi-property value-object (`Money { Amount, Currency }`) → unwrap ambiguous → **no rewrite**. The generator emits the validator against the wrapper as today, which produces the existing CS0019 / type-mismatch compile error.

Layered on top: `ZV0016` Warning when a built-in operand-taking validator is declared on a property whose type carries `[ValueObject]` but has more than one property. Tells the user *why* the rewrite didn't help.

```csharp
private static readonly DiagnosticDescriptor ZV0016 = new DiagnosticDescriptor(
    id: "ZV0016",
    title: "Value-object with multiple properties can't be auto-unwrapped",
    messageFormat:
        "Property '{0}' of type '{1}' carries a built-in validator but '{1}' is a multi-property value-object ({2} properties). " +
        "Auto-unwrap requires exactly one underlying property. " +
        "Either declare the validator on a single-property value-object, or use [Must] / [CustomValidation] with a custom predicate.",
    category: "ZeroAlloc.Validation",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

ID `ZV0016` fills the gap after `ZV0014` (struct target — shipped 1.4.0). `ZV0015` is the existing pipeline-behavior `Order` collision diagnostic.

**Considered and rejected:**

- **Pick a property by convention** (named `Value`, or first declared). Surprises adopters who renamed it; quietly breaks on field reorder. Rejected.
- **`MemberOf` hint on the validator attribute** (e.g. `[GreaterThan(0, MemberOf = nameof(Money.Amount))]`). API expansion only justified if multi-property value-objects with built-in validators is a load-bearing use case. No current consumer; YAGNI.

### D-4: rewrite at the property-access construction site, not per validator

`RuleEmitter` builds a `propAccess` expression (`$"{modelParamName}.{prop.Name}"`) before iterating the property's rules. Each rule-emission method bakes `propAccess` into the comparison/regex/length code (`$"if ({propAccess} <= 0)"`).

The fix is **one rewrite at the `propAccess` builder**:

```csharp
var unwrapMember = GetValueObjectUnwrapMember(prop.Type);
var propAccess = unwrapMember is not null
    ? $"{modelParamName}.{prop.Name}.{unwrapMember}"
    : $"{modelParamName}.{prop.Name}";
```

After this single change, every operand-taking validator emission method works unchanged — they all consume `propAccess` and don't care that it now accesses through a field. Predicate-validator emission methods (which take the property's identity, not its operand-form access) are unaffected.

**Why not per-validator-method opt-in:** would require touching every `Emit{Validator}` method (dozens of files / methods), each potentially with a slightly different shape. Fragile across new validators added in the future. The single-point rewrite scales automatically.

### D-5: helper placement + property-name derivation

```csharp
// src/ZeroAlloc.Validation.Generator/RuleEmitter.cs

private const string ValueObjectAttributeFqn = "ZeroAlloc.ValueObjects.ValueObjectAttribute";

/// <summary>
/// If <paramref name="type"/> is a single-property value-object (decorated
/// with <c>[ZeroAlloc.ValueObjects.ValueObject]</c> and declaring exactly one
/// public instance property), returns that property's name. Returns null for
/// everything else — class types, primitives, multi-property value-objects,
/// or types without the marker attribute.
/// </summary>
private static string? GetValueObjectUnwrapMember(ITypeSymbol type)
{
    if (type is not INamedTypeSymbol named) return null;

    var hasMarker = named.GetAttributes()
        .Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            ValueObjectAttributeFqn,
            StringComparison.Ordinal));
    if (!hasMarker) return null;

    var properties = named.GetMembers()
        .OfType<IPropertySymbol>()
        .Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
        .ToArray();

    return properties.Length == 1 ? properties[0].Name : null;
}

/// <summary>
/// True when <paramref name="type"/> carries <c>[ValueObject]</c>; used to fire
/// the multi-property diagnostic ZV0016 even when <see cref="GetValueObjectUnwrapMember"/>
/// returns null (i.e. multi-property case).
/// </summary>
private static bool HasValueObjectAttribute(ITypeSymbol type) =>
    type is INamedTypeSymbol named
    && named.GetAttributes().Any(a => string.Equals(
        a.AttributeClass?.ToDisplayString(),
        ValueObjectAttributeFqn,
        StringComparison.Ordinal));
```

`GetValueObjectUnwrapMember` returns the actual property symbol name (not hardcoded `"Value"`). Works whether the user wrote `int Value`, `string Name`, or any single-prop wrapper shape.

## Design

### Files touched

- **MOD:** `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` — add helper pair + the rewrite at the `propAccess` builder + `ZV0016` descriptor + diagnostic emission.
- **MOD:** `src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md` — add `ZV0016` row.
- **NEW:** `tests/ZeroAlloc.Validation.Tests/Integration/ValueObjectPropertyValidationTests.cs` — runtime behaviour, 4 cases.
- **NEW:** `tests/ZeroAlloc.Validation.Tests/Generator/ValueObjectPropertyDiagnosticTests.cs` — generator-snapshot, 4 cases.
- **MOD:** `docs/getting-started.md` — "Validating value-object properties" subsection.
- **MOD:** `docs/diagnostics.md` — `## ZV0016` entry + summary-table row.
- **MOD:** `docs/backlog.md` — mark B1 ✅ shipped 1.5.0 on release-PR merge.

### Test surface

**Integration** (`ValueObjectPropertyValidationTests.cs`, 4 cases):

| # | Shape | Assertion |
|---|---|---|
| 1 | `[Validate] X([property: GreaterThan(0)] CustomerId Id)` — single-property value-object wrapping int. Happy path: `Id = 42`. | `IsValid == true`; `Failures.IsEmpty` |
| 2 | Same shape, sad path: `Id = 0`. | `IsValid == false`; `Failures.Length == 1`; `Failures[0].PropertyName == "Id"` |
| 3 | `[Validate] X([property: NotEmpty] Username Name)` — single-prop value-object wrapping `string`. `[Theory]` over `("alice", true)` and `("", false)`. | matches expected |
| 4 | `[Validate] X([property: InclusiveBetween(1, 100)] PageNumber Page)` — single-prop value-object wrapping int. `[Theory]` over `(50, true)`, `(0, false)`, `(101, false)`. | matches expected |

Inline models declared in the test file. Each marked `[ZeroAlloc.ValueObjects.ValueObject]` and exposing a single public property.

**Generator-snapshot** (`ValueObjectPropertyDiagnosticTests.cs`, 4 cases):

| # | Source | Expected generated output / diagnostic |
|---|---|---|
| 1 | `[Validate] X([property: GreaterThan(0)] CustomerId Id)` where `CustomerId : [ValueObject] readonly partial struct { int Value }` | generated `.g.cs` contains `instance.Id.Value > 0`; no `ZV0016` diagnostic |
| 2 | Same shape but `Id` is `int Id` (no value-object) — regression net | generated `.g.cs` contains `instance.Id > 0`; no diagnostic |
| 3 | `[GreaterThan(0)] Money Total` where `Money : [ValueObject] { decimal Amount, string Currency }` | `ZV0016` Warning emitted; generated `.g.cs` keeps `instance.Total > 0` (no rewrite — downstream CS0019) |
| 4 | `[Must(typeof(MyValidator))] CustomerId Id` — predicate validator on a single-prop value-object | generated `.g.cs` passes `instance.Id` (the wrapper) to the predicate — predicate-validator carve-out preserved |

Existing tests (the ~937 already-shipped suite + the B2/B3 additions) act as the regression net for primitive-typed and class-typed paths.

### Backward compatibility

Strictly additive:

- Class-typed and primitive-typed property emissions are byte-identical to today (`GetValueObjectUnwrapMember` returns `null`, generator falls through to current `propAccess` builder).
- `[ZeroAlloc.ValueObjects.ValueObject]` doesn't currently fire any ZA.Validation behaviour — adopters who already have both packages and primitive-typed validated requests see no diff.
- `ZV0016` is new; no existing code can have been firing it.

SemVer: minor bump (`1.4.1` → `1.5.0`).

## Out of scope

- **Multi-property value-object support with `MemberOf` hint.** Backlog item if surfaced by a real consumer.
- **Collection-element value-objects** (`IReadOnlyList<MyValueObject>` where `MyValueObject` is `[ValueObject]`). The B3 fix already handles the value-type element case for `[Validate]`-decorated elements; `[ValueObject]`-only elements would need an analogous unwrap rule at the nested-validator emission sites. Separate enhancement.
- **Predicate-validator opt-in unwrap.** A future `[MustOnValue]` or similar attribute could request unwrap-before-predicate behaviour. Not in this PR.
- **Coordinated `IValueObject<T>` marker interface in ZA.ValueObjects.** D-1's "right long-term API" — coordinated change across both packages. Not B1's concern.

## Files touched (final count)

- **MOD:** 1 generator file (`RuleEmitter.cs`) — ~30 LOC + 1 descriptor
- **MOD:** 1 analyzer-release manifest
- **NEW:** 2 test files — ~250 LOC (4 + 4 cases)
- **MOD:** 2 doc files (`getting-started.md`, `diagnostics.md`)
- **MOD:** `docs/backlog.md` — mark B1 shipped on release-PR merge

Total commit footprint: ~300 LOC including tests.
