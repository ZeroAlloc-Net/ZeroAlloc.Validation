# Nested-Validator Value-Type Null-Guard — Design

**Date:** 2026-05-27
**Scope:** ZeroAlloc.Validation generator emits `if (... is not null)` guards at two nested-validator emission sites in `RuleEmitter.cs`. The guards are valid for class types and `Nullable<T>` but produce `CS0037: Cannot convert null to 'T' because it is a non-nullable value type` when the property/element type is a non-nullable value type. This is a regression introduced (latently) by 1.4.0's struct-target widening — the emission paths were never exercised against value-type nested types before. Fix is a one-line guard helper that suppresses the null check when the type is a non-nullable value type. Ships as **ZeroAlloc.Validation 1.4.1** (patch).

## Background

1.4.0 widened `[Validate]` from `Class` to `Class | Struct` ([PR #42](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/pull/42)). The downstream rule-emission paths were assumed `TypeKind`-agnostic, and for **top-level** `[Validate]` types they are: the generated `XValidator.Validate(X instance)` overload works whether X is a class, record, struct, or record struct.

Two emission paths break, however, when a `[Validate]` type contains a **nested** validated reference of value-type kind:

- `RuleEmitter.cs:341-355` — `EmitCollectionValidatorForProp`, emitted for properties of `IReadOnlyList<TItem>` (or similar collections) where `TItem` is also `[Validate]`-decorated. The inner foreach unconditionally emits `if ({varName}Item is not null)`. When `TItem` is a non-nullable value type (`readonly record struct OrderItem(...)`), this is a compile error.
- `RuleEmitter.cs:317` — `EmitNestedValidatorForProp`, emitted for scalar properties carrying `[ValidateWith(...)]` whose type is itself `[Validate]`-decorated. Same unconditional `is not null` check, same failure mode if the property type is a non-nullable value type.

Surfaced 2026-05-27 while attempting to migrate `za-clean`'s `CreateOrderCommand` + `OrderItem` from `sealed record` to `readonly record struct` ([ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates), follow-up branch `feat/migrate-validate-types-to-record-struct`). `CreateOrderCommand` migrates fine; `OrderItem` (which sits inside `IReadOnlyList<OrderItem> Items` on `CreateOrderCommand`) trips `CS0037` from the generator's collection-emission path.

`za-vertical-slice` doesn't currently expose this because its six `[Validate]` request types have no nested `[Validate]` references. The latent scalar-property bug (line 317) has no current consumer either, but the code path is identical and deserves the same one-line fix.

## Goal

Both nested-validator emission sites recognise non-nullable value types and skip the `is not null` guard for them. Existing class-type consumers see no behaviour change. The fix is the minimum diff that unblocks the za-clean template migration without changing the generator's API surface or any public behaviour.

## Decisions

### D-1: scope — both emission sites in one fix

Fix both `EmitCollectionValidatorForProp` (line 346) and `EmitNestedValidatorForProp` (line 317) in 1.4.1. They are the same bug class through the same predicate; a single helper covers both. Leaving the scalar-property site latent would close the za-clean blocker but leave a known-broken path for the next consumer to discover.

**Considered and rejected:**

- **Just the collection site.** Smaller diff, ships faster, but punts the scalar bug to a future 1.4.2 with no shipping consumer to gate that release.

### D-2: guard predicate — non-nullable value type only

The guard is suppressed when **and only when** `IsValueType && OriginalDefinition.SpecialType != System_Nullable_T`. Class types keep the guard (runtime null check, current behaviour). `Nullable<T>` keeps the guard — `is not null` lowers to `.HasValue`, which is the correct behaviour for "skip validation when the underlying struct isn't present."

**Why not drop the guard for `Nullable<T>` too:** dropping it would force the generated `Validate(default)` call against an unset Nullable, which is wrong — the validator would run against a zero-initialised struct rather than reporting a missing-value failure (or, more typically, silently skipping it). Keeping `is not null` for `Nullable<T>` matches the design intent of the original guard.

### D-3: helper placement — static method on `RuleEmitter`

Single static method `NeedsNullGuard(ITypeSymbol type)`. Called twice. Inline expression at each call site would duplicate the predicate and make a future tweak risky.

## Design

### Helper

```csharp
// src/ZeroAlloc.Validation.Generator/RuleEmitter.cs

/// <summary>
/// Returns true when an `is not null` guard against a value of this type
/// would compile and have meaningful runtime semantics. Class types always
/// need the guard. `Nullable<T>` keeps it (`is not null` lowers to .HasValue).
/// Non-nullable value types CANNOT take the guard (CS0037), so it is omitted.
/// </summary>
private static bool NeedsNullGuard(ITypeSymbol type) =>
    !type.IsValueType
    || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
```

### Collection site — `EmitCollectionValidatorForProp`

Current:

```csharp
sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
sb.AppendLine("        {");
sb.AppendLine($"            int {varName}Idx = 0;");
sb.AppendLine($"            foreach (var {varName}Item in {modelParamName}.{propName})");
sb.AppendLine("            {");
sb.AppendLine($"                if ({varName}Item is not null)");
sb.AppendLine("                {");
sb.AppendLine($"                    var {varName}Result = _{camelC}Validator.Validate({varName}Item);");
sb.AppendLine($"                    foreach (ref readonly var f in {varName}Result.Failures)");
sb.AppendLine($"                        _buf.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ ... }});");
sb.AppendLine("                }");
sb.AppendLine($"                {varName}Idx++;");
sb.AppendLine("            }");
sb.AppendLine("        }");
```

Fixed:

```csharp
var elementType = ((INamedTypeSymbol)collProp.Type).TypeArguments[0]; // element T from IReadOnlyList<T>
var needsItemGuard = NeedsNullGuard(elementType);

sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
sb.AppendLine("        {");
sb.AppendLine($"            int {varName}Idx = 0;");
sb.AppendLine($"            foreach (var {varName}Item in {modelParamName}.{propName})");
sb.AppendLine("            {");
if (needsItemGuard)
{
    sb.AppendLine($"                if ({varName}Item is not null)");
    sb.AppendLine("                {");
}
sb.AppendLine($"                    var {varName}Result = _{camelC}Validator.Validate({varName}Item);");
sb.AppendLine($"                    foreach (ref readonly var f in {varName}Result.Failures)");
sb.AppendLine($"                        _buf.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ ... }});");
if (needsItemGuard)
{
    sb.AppendLine("                }");
}
sb.AppendLine($"                {varName}Idx++;");
sb.AppendLine("            }");
sb.AppendLine("        }");
```

Indentation drift in the generated source when the guard is omitted is harmless — the C# compiler ignores it; nobody hand-reads the .g.cs.

The outer `if ({modelParamName}.{propName} is not null)` guards the **collection itself**, which is always a reference type (`IReadOnlyList<T>`, `List<T>`, etc.) — that one stays unconditional.

### Scalar site — `EmitNestedValidatorForProp`

Same pattern at `RuleEmitter.cs:317`:

```csharp
var needsPropGuard = NeedsNullGuard(nestedProp.Type);

if (needsPropGuard)
{
    sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
    sb.AppendLine("        {");
}
// …emit the Validate call + Failures forwarding…
if (needsPropGuard)
{
    sb.AppendLine("        }");
}
```

### Test surface

New file `tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs` with five snapshot cases following `StructValidationDiagnosticTests.cs`'s shape (Roslyn `CSharpCompilation.Create` + `CSharpGeneratorDriver` + assertions on `Diagnostics` + generated source text):

| # | Source | Expected |
|---|---|---|
| 1 | `[Validate]` outer with `IReadOnlyList<X> Items` where `X` is `[Validate] readonly record struct` | compiles cleanly; generated .g.cs does **not** contain `if (_c0Item is not null)` |
| 2 | Same shape but `X` is `[Validate] class` | compiles; generated .g.cs **does** contain `if (_c0Item is not null)` |
| 3 | `[Validate]` outer with `IReadOnlyList<X?> Items` where `X` is `[Validate] readonly record struct` (Nullable<X> elements) | compiles; generated .g.cs **does** contain `if (_c0Item is not null)` (translates to `.HasValue`) |
| 4 | `[Validate]` outer with `[ValidateWith(typeof(InnerValidator))] X Inner` where `X` is `[Validate] readonly record struct` | compiles; generated .g.cs does **not** contain `if (instance.Inner is not null)` for that property |
| 5 | Same shape but `X` is class | compiles; generated .g.cs **does** contain the null guard |

Existing `CollectionValidationTests.cs` and the wider integration suite cover the runtime side and act as the regression net for class-typed paths.

### Versioning

`1.4.0` → `1.4.1`. Patch — pure bug fix; the only observable change is "code that previously failed to compile now compiles correctly." No public API changes, no new diagnostics, no new analyzer-release entry needed.

Conventional commit:

```
fix(generator): skip `is not null` guard on nested validators when the type is a non-nullable value type
```

### Files touched

- **MOD:** `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` — add `NeedsNullGuard` helper, wrap the two emission sites (~15 lines + the helper).
- **NEW:** `tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs` — 5 snapshot tests (~150 lines).
- **MOD:** `docs/backlog.md` — add B3 entry; mark shipped on release-PR merge.

Total commit footprint: ~200 LOC including tests.

## Backward compatibility

Strictly additive at the generator-output level: any source that previously compiled still compiles to identical .g.cs. The change is **purely subtractive** in the value-type case — code paths that previously failed `csc` now succeed. Existing class-typed consumers see byte-identical generator output.

SemVer: patch (`1.4.0` → `1.4.1`).

## Out of scope

- **A diagnostic for the latent bug.** No `ZV00NN` warning when the generator emits the value-type-aware path; the case is "things compile now where they didn't before," not "you've done something dubious."
- **Generic type parameters as element types.** A `[Validate]` type with `IReadOnlyList<T>` where T is an unconstrained generic parameter is rare and not currently a documented pattern. If it shows up, it's a separate brainstorm.
