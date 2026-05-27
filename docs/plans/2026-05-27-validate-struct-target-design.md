# `[Validate]` on `struct` / `record struct` — Design

**Date:** 2026-05-27
**Scope:** Widen the `[Validate]` attribute target from `Class` to `Class | Struct`, extend the source generator's syntax predicate to match `StructDeclarationSyntax` / `RecordStructDeclarationSyntax`, and emit `ZV0014` as a Warning when `[Validate]` decorates a non-readonly struct. Closes backlog item B2 in `docs/backlog.md`. Ships as **ZeroAlloc.Validation 1.3.0** (additive minor — existing class/record consumers see no behaviour change).

## Background

`ValidateAttribute` ships with `AttributeUsage(AttributeTargets.Class)`. C# encodes `class` and `record` as `Class`, but `struct` and `record struct` as `Struct`. As a result, `[Validate]` rejects struct-shaped request types with **CS0592: Attribute 'Validate' is not valid on this declaration type. It is only valid on 'class' declarations.**

This surfaced 2026-05-26 while building the `za-vertical-slice` template (ZeroAlloc.Templates 0.7.0). The vertical-slice idiom prefers `readonly record struct` request types for the zero-allocation story; the template's four request types had to be widened to `sealed record` (class) for the sole reason of attaching `[Validate]`. That widening costs the per-request stack-allocation savings the struct form would deliver. The same pattern hits any allocation-sensitive consumer pairing `[Validate]` with hot-path command dispatch.

The downstream generator (`ValidatorGenerator.cs` + `RuleEmitter.cs`) walks `INamedTypeSymbol.GetMembers()` for properties and emits `Validate(T instance)` against the named symbol. The walk is `TypeKind`-agnostic — once the attribute target accepts structs, the existing emission paths produce a working validator with no further changes.

## Goal

`[Validate]` decorates all four C# shapes — `class`, `record`, `struct`, `record struct` — and the source generator emits a working `XValidator : ValidatorFor<X>` for each. Non-readonly structs raise `ZV0014` (Warning) to flag the staleness hazard (a caller can mutate the instance after validation returns success and before the consumer reads it).

## Decisions

### D-1: target widening — `Class | Struct`, not gated by `readonly`

Accept all four shapes. Restricting `[Validate]` to `readonly struct` / `readonly record struct` via a hard refusal in the generator would be paternalistic for a v1.x minor bump — legitimate uses exist (builder-style mutable structs frozen by a wrapper before validation, or short-lived in-method validation where mutation isn't possible). The warning (D-3) gives the loud signal at compile time without blocking the build.

**Considered and rejected:**

- **`Class | Struct` restricted to `readonly` structs only** (hard error on non-readonly). Safer but blocks legitimate cases and converts a Warning-grade hazard into a build-stopping rule. Rejected.
- **`Struct` only when `readonly`** — same shape as above but enforced via an analyzer check that fails before emission. Same issue, same rejection.

### D-2: validator method signature — pass-by-value, no new overload

Keep the existing `ValidatorFor<T>.Validate(T instance)` signature. The generator emits the same `public override ValidationResult Validate(T instance)` for all four shapes. For struct T this means a defensive copy on every call.

**Why:** the "ZeroAlloc" promise is about **heap** allocations, not struct copies. Request structs typical of `[Validate]` consumers are small (the `za-vertical-slice` PlaceOrderCommand is `(int CustomerId, decimal Total)` = 20 bytes); the stack copy is below noise on the measured allocation profile (Validator_Generated benchmark at 2.18 ns / 0 B). Adding an `in T` overload to `ValidatorFor<T>` would expand the public surface for theoretical wins that aren't currently load-bearing.

**Considered and rejected:**

- **Add `Validate(in T)` to `ValidatorFor<T>`.** Generator emits `in T` for struct T, plain T for class T. Saves the copy but expands the abstract base's API. Deferrable — if a future consumer measures the copy cost as load-bearing, ship it then as a 1.4.x minor.
- **Don't derive from `ValidatorFor<T>` for structs.** Generator emits a free-standing sealed validator with `Validate(in T)` for struct T. Cleanest signature but sacrifices polymorphism (validators can no longer be stored in a `Dictionary<Type, ValidatorFor<>>`-style registry). Rejected.

### D-3: non-readonly struct fires `ZV0014` Warning

New diagnostic descriptor in `ValidatorGenerator.cs`:

- ID: `ZV0014` (fills the gap between shipped `ZV0013` and `ZV0015`)
- Category: `ZeroAlloc.Validation`
- Severity: `Warning` (build-visible, opt-out via `#pragma warning disable ZV0014` or `<NoWarn>$(NoWarn);ZV0014</NoWarn>`)
- Title: "[Validate] on non-readonly struct"
- Message: "Struct '{0}' is decorated with [Validate] but is not declared `readonly`. A caller can mutate the instance between the validator returning success and the consumer reading the value, making validation results stale. Declare the struct as `readonly struct` or `readonly record struct`."

Fires during `RegisterSourceOutput` after resolving `INamedTypeSymbol`, when `TypeKind == Struct && !IsReadOnly`. Generator still emits the validator — warning is a soft signal, not a refusal.

**Why Warning, not Error or Info:**

- **Warning** gets caught at PR review (visible in CI logs and IDE), opt-out via single-line pragma when the consumer cooperates with the hazard.
- **Error** is paternalistic for a v1.x minor; blocks legitimate cases (D-1 rationale).
- **Info** loses CI visibility — IDE-only signal that's easy to miss in code review.

## Design

### Files touched

- `src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs` — widen `AttributeTargets.Class` to `AttributeTargets.Class | AttributeTargets.Struct`.
- `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` — widen the `ForAttributeWithMetadataName` predicate to also match `StructDeclarationSyntax` / `RecordStructDeclarationSyntax`; add the `ZV0014` `DiagnosticDescriptor`; emit the warning conditionally during `RegisterSourceOutput`.
- `src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md` — add the `ZV0014` row.
- `tests/ZeroAlloc.Validation.Tests/Integration/StructValidationTests.cs` (NEW) — 4 integration `[Fact]`s covering `readonly struct` + `readonly record struct` happy/sad paths.
- `tests/ZeroAlloc.Validation.Tests/Generator/StructValidationDiagnosticTests.cs` (NEW) — 5 generator-level diagnostic tests covering the matrix of shape × readonly-ness.
- `docs/getting-started.md` — callout that `[Validate]` accepts `class` / `record` / `readonly struct` / `readonly record struct`, with the `ZV0014` warning for non-readonly structs.
- `docs/error-messages.md` — `ZV0014` section: hazard description + fix (`readonly struct`).
- `docs/backlog.md` — mark B2 ✅ shipped (1.3.0) on release-PR merge.

### Attribute target

```csharp
// src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs
namespace ZeroAlloc.Validation;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class ValidateAttribute : Attribute
{
    public bool StopOnFirstFailure { get; set; }
}
```

### Generator syntax predicate

```csharp
// src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs (Initialize)
var validateClasses = context.SyntaxProvider
    .ForAttributeWithMetadataName(
        ValidateAttributeFqn,
        // All four C# shapes — class, record (class), struct, record struct —
        // hit the same emission path; the downstream generator walks symbol
        // properties identically regardless of TypeKind.
        predicate: static (node, _) =>
            node is ClassDeclarationSyntax
                 or RecordDeclarationSyntax
                 or StructDeclarationSyntax
                 or RecordStructDeclarationSyntax,
        transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);
```

### `ZV0014` diagnostic

```csharp
// src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs
private static readonly DiagnosticDescriptor ZV0014 = new DiagnosticDescriptor(
    id: "ZV0014",
    title: "[Validate] on non-readonly struct",
    messageFormat:
        "Struct '{0}' is decorated with [Validate] but is not declared `readonly`. " +
        "A caller can mutate the instance between the validator returning success " +
        "and the consumer reading the value, making validation results stale. " +
        "Declare the struct as `readonly struct` or `readonly record struct`.",
    category: "ZeroAlloc.Validation",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

Reported during the `RegisterSourceOutput` callback (after `ForAttributeWithMetadataName` resolves the symbol):

```csharp
if (symbol.TypeKind == TypeKind.Struct && !symbol.IsReadOnly)
{
    ctx.ReportDiagnostic(Diagnostic.Create(
        ZV0014,
        symbol.Locations.FirstOrDefault() ?? Location.None,
        symbol.Name));
}
```

Generator emission proceeds regardless — the warning is informational.

### Test surface

**Integration** (`StructValidationTests.cs`): four `[Fact]`s — two shapes × happy / sad paths. Inline model declarations:

```csharp
[Validate]
public readonly record struct RrsCommand([property: GreaterThan(0)] int Total);

[Validate]
public readonly struct RsCommand
{
    public int Total { get; }
    public RsCommand(int total) => Total = total;
}
```

Assertions: `Validate` returns `IsValid == true` for valid input; `IsValid == false` + `Failures[0].PropertyName == "Total"` for invalid.

**Generator diagnostic** (`StructValidationDiagnosticTests.cs`): five cases, snapshot pattern following `GeneratorDiscoveryTests.cs`:

| Source | Expected diagnostics |
|---|---|
| `[Validate] readonly record struct X(...)` | none |
| `[Validate] readonly struct X { ... }` | none |
| `[Validate] record struct X(...)` | `ZV0014` |
| `[Validate] struct X { ... }` | `ZV0014` |
| `[Validate] class X { ... }` | none (regression net) |

**Existing tests** (class + record) act as the regression net for the legacy paths — no edits needed.

### Backward compatibility

Strictly additive:

- Existing `[Validate] class` / `[Validate] record` consumers see zero behaviour change.
- `ZV0014` is a new diagnostic — by definition no existing code can have been emitting it.
- `ValidatorFor<T>` base signature is unchanged (D-2).

SemVer: minor bump (`1.2.0` → `1.3.0`).

## Out of scope (deferred)

- **`Validate(in T)` overload on `ValidatorFor<T>`** (D-2 alternative). Defer until a consumer measures the struct-copy cost as load-bearing.
- **Value-object-aware property validators** — backlog B1, separate brainstorm session, separate 1.x minor.
- **Migrating the `za-vertical-slice` template's request types from `sealed record` back to `readonly record struct`.** Happens in `ZeroAlloc.Templates` after 1.3.0 propagates to NuGet — separate PR there.

## Files touched (final count)

- **MOD:** 1 attribute file
- **MOD:** 1 generator file (+1 descriptor + diagnostic report)
- **MOD:** 1 analyzer-release manifest
- **NEW:** 2 test files (~9 tests total)
- **MOD:** 2 doc files (getting-started + error-messages)
- **MOD:** `docs/backlog.md` (mark B2 shipped on release)

Total commit footprint: ~250 LOC including tests.
