# ZeroAlloc.Validation — Backlog

Candidate enhancements identified during real-world usage. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

---

## B3 — Nested-validator emits `is not null` against value-type elements

**What.** When a `[Validate]` type contains `IReadOnlyList<T>` (where T is `[Validate]`-decorated) or a scalar property whose type is `[Validate]`-decorated, the generator emits `if (... is not null)` unconditionally. Class T and `Nullable<T>` compile correctly; **non-nullable value types fail with `CS0037`** (cannot convert null to a non-nullable value type).

**Why.** Surfaced 2026-05-27 while migrating `za-clean`'s `CreateOrderCommand` + `OrderItem` from `sealed record` to `readonly record struct` ([ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates) follow-up to the 1.4.0 ship). `CreateOrderCommand` migrates fine; `OrderItem` (inside `IReadOnlyList<OrderItem> Items`) trips the generator. Same code path emits the same guard for scalar `[Validate]` nested properties — latent bug there too, fixed in the same release.

**Sketch.** Predicate `NeedsNullGuard(ITypeSymbol)` returning `false` only for non-nullable value types. Applied at both nested emission sites in `RuleEmitter.cs` (`EmitCollectionValidatorForProp` and `EmitNestedValidatorForProp`). Class types keep the guard. `Nullable<T>` doesn't currently reach the emission site at all — `HasValidateAttribute` filtering on the wrapped type rejects it upstream — but the predicate's `Nullable<T>` arm stays as a defensive belt-and-braces against any future loosening of that filter.

**Tradeoff / risks.**

- Indentation drift in the generated `.g.cs` when the guard is omitted (no harm — `csc` ignores indentation; nobody hand-reads generator output).
- Public API surface unchanged; pure subtractive fix at the generator-output level.

**Graduation signal.** Same template surfaced the bug; landing it is the graduation. Ships as **1.4.1** (patch).

---

## ~~B1 — Value-object aware property validators~~ — ✅ shipped 1.5.0 (2026-05-27)

**Shipped:** Three private helpers in `RuleEmitter.cs` (`GetValueObjectUnwrapMember` / `HasValueObjectAttribute` / `BuildPropertyAccess`) drive the unwrap at three call sites — the two per-property emission paths and `BuildPropertyValueExpr`. Built-in operand-taking validators (`[GreaterThan]`, `[NotEmpty]`, `[Matches]`, `[InclusiveBetween]`, …) participate uniformly. `[Must]` keeps the wrapper via an explicit `rawPropAccess` parameter routed through `BuildCondition`'s switch. `[CustomValidation]` and `[ValidateWith]` weren't affected — they don't route through `BuildCondition` at all. New `ZV0016` Warning fires when a property carries a built-in validator and the type is a multi-property `[ValueObject]` (e.g. `Money { Amount, Currency }`); the diagnostic is reported only from the sync emission path (nullable `SourceProductionContext?` threading) so it fires exactly once per offending property even when `Validate` + `ValidateAsync` both emit. Shipped as ZeroAlloc.Validation 1.5.0 via [PR #46](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/pull/46). Strictly additive — existing class/primitive consumers see byte-identical generator output.

**Design + plan:** [`docs/plans/2026-05-27-value-object-property-validators-design.md`](plans/2026-05-27-value-object-property-validators-design.md) + [`docs/plans/2026-05-27-value-object-property-validators.md`](plans/2026-05-27-value-object-property-validators.md).

**Decisions worth flagging** (durable record for whoever picks up the next ZA.Validation enhancement):

- **FQN-based attribute detection.** ZA.Validation never references ZA.ValueObjects at runtime — only matches `ZeroAlloc.ValueObjects.ValueObjectAttribute` by metadata name. Adopters who don't pull in ZA.ValueObjects pay nothing.
- **Single-property requirement.** Multi-property value-objects (`Money`-style) fall through with `ZV0016` Warning and the build fails on the underlying type mismatch. Use `[Must]` or `[CustomValidation]` for those.
- **Predicate-validator carve-out is explicit, not automatic.** The design doc initially claimed predicate validators "naturally don't participate" — wrong. `[Must]` routes through `BuildCondition` and the rewrite leaked in. Phase 5's regression test caught it; fix was the new `rawPropAccess` parameter in `BuildCondition`. Future predicate-like validators that route through the same switch must use `rawPropAccess`.

<details>
<summary>Original B1 proposal (kept for context)</summary>

**What.** Teach the `[Validate]` generator to recognize properties whose declared type is a `ZeroAlloc.ValueObjects` `[ValueObject]` partial struct, and emit comparison/range/predicate validators (`[GreaterThan]`, `[LessThan]`, `[InRange]`, `[NotEmpty]`, …) against the unwrapped underlying value rather than the wrapper. So this becomes legal:

```csharp
[Validate]
public readonly record struct PlaceOrderCommand(
    [property: GreaterThan(0)] CustomerId CustomerId,
    [property: GreaterThan(0)] decimal Total)
    : IRequest<Result<OrderId, Error>>;
```

Today the generator can only emit a `>` against a primitive — a `CustomerId` wrapping an `int` doesn't satisfy the comparison, so the request must be modelled with `int CustomerId` and the handler manually wraps to `new CustomerId(...)`. This weakens the request-side type signal exactly where validation should be reinforcing it.

**Why.** Friction surfaced 2026-05-26 building the `za-vertical-slice` template's `PlaceOrder` slice (ZeroAlloc.Templates 0.4.0). The vertical-slice idiom pushes hard on "request type IS the contract"; being forced to fall back to raw `int` for any property that participates in validation undermines that. The same pattern almost certainly hits every consumer that combines `[Validate]` with `[ValueObject]`-typed identifiers.

**Sketch.** During property resolution in the generator:

- If the property type is a struct from a referenced compilation with `[ValueObject]` (or shape: partial struct with a single readable `Value` property of comparable primitive type plus a matching constructor),
- treat the property as the underlying primitive for the purposes of emitting validator comparisons,
- emit `prop.Value` (or the resolved member name) as the comparable target.

Non-comparable / multi-field value-objects (e.g. `Money(Amount, Currency)`) fall back to the existing predicate-validator path; only single-primitive wrappers participate.

**Tradeoff / risks.**

- Couples `ZeroAlloc.Validation`'s generator to the generated shape of `ZeroAlloc.ValueObjects`. A future ValueObjects rename of `Value` or a layout change could silently break this. Two mitigations to evaluate at design time:
  - (a) duck-type on "single public readable property whose type matches the validator's comparison target" — robust to attribute renames, but matches accidentally on unrelated structs;
  - (b) detect `[ValueObject]` by metadata name and read a generator-emitted marker attribute on the unwrap member — tighter coupling, but explicit.
- Bigger alternative (option B from the brainstorm): push invariants into `ZeroAlloc.ValueObjects` itself (`[Positive]` on the partial struct → guarded ctor). Stronger correctness story but a much larger API change, and conflicts with EF Core "construct-with-sentinel, DB picks id" patterns (`new OrderId(0)` would fail at construction). This backlog item deliberately scopes to the lighter-touch option.

**Graduation signal.** A second consumer codebase hits the same papercut, OR `za-vertical-slice` reaches v1 and the workaround (raw primitive in request, wrap in handler) is judged too lossy to ship as the documented convention.

</details>

---

## ~~B2 — `[Validate]` on `record`, `record struct`, and `struct` targets~~ — ✅ shipped 1.4.0 (2026-05-27)

**Shipped:** `AttributeTargets.Class | AttributeTargets.Struct` widening on `ValidateAttribute`, generator syntax predicate extended to cover `StructDeclarationSyntax` (`record struct` is already represented by `RecordDeclarationSyntax` in Roslyn — no separate type), plus the `ZV0014` Warning when `[Validate]` decorates a non-readonly struct. Shipped as ZeroAlloc.Validation 1.4.0 via [PR #42](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/pull/42) (release [PR #43](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/pull/43)). Strictly additive — existing class/record consumers see no behaviour change.

**Design + plan:** [`docs/plans/2026-05-27-validate-struct-target-design.md`](plans/2026-05-27-validate-struct-target-design.md) + [`docs/plans/2026-05-27-validate-struct-target.md`](plans/2026-05-27-validate-struct-target.md).

**Decisions worth flagging** (durable record for future maintainers):

- Pass-by-value `Validate(T instance)` retained for structs — no `Validate(in T)` overload on `ValidatorFor<T>`. Stack copy is below noise on the measured profile (Validator_Generated at 2.18 ns / 0 B in the za-vertical-slice benchmarks). Deferred until a consumer measures it as load-bearing.
- `ZV0014` is a **Warning**, not Error. Hard refusal would block legitimate mutable-then-frozen patterns; pragma-disable opt-out is trivial.
- No restriction to `readonly` — all four shapes (`class` / `record` / `struct` / `record struct`) participate; ZV0014 raises the loud signal at compile time on non-readonly structs without blocking the build.

---

<details>
<summary>Original B2 proposal (kept for context)</summary>

**What.** Allow `[Validate]` to decorate `record` (class) **and** `struct` / `record struct` declarations, not just `class`. Today the attribute is declared with `AttributeTargets.Class`, so:

```csharp
[Validate]
public readonly record struct PlaceOrderCommand(             // CS0592
    [property: GreaterThan(0)] int CustomerId,
    [property: GreaterThan(0)] decimal Total);
```

…produces `CS0592: Attribute 'Validate' is not valid on this declaration type. It is only valid on 'class' declarations.` `sealed record` (record class) compiles, but `record struct` and plain `struct` do not. The generator itself almost certainly produces correct validator code for any of these shapes — the restriction is at the attribute-target level.

**Why.** Friction surfaced 2026-05-26 building the `za-vertical-slice` template's `PlaceOrder` / `GetOrder` / `ListOrders` / `CancelOrder` slices (ZeroAlloc.Templates 0.4.0). The vertical-slice idiom prefers request types as `readonly record struct` for the small-allocation story; being forced to widen them to `sealed record` (class) for the sole reason of attaching `[Validate]` is exactly the kind of papercut that erodes the "ZeroAlloc" promise at the entry-point of the pipeline. Same pattern hits anyone building allocation-sensitive request types (high-throughput message handlers, hot-path command dispatch).

**Sketch.**

- Change the `[Validate]` `AttributeUsage` to `AttributeTargets.Class | AttributeTargets.Struct`. C# treats `record` and `record struct` as class / struct respectively, so this single change unlocks all four shapes.
- Audit the generator for any code that assumes the target is a class (e.g. `: TypeKind.Class`-only checks, nullability of the validated instance). For `struct` / `record struct`, instances are non-nullable by value semantics — the generated validator probably already handles this correctly, but a snapshot test should pin it.
- Add four positive snapshot tests covering each shape (`class`, `record`, `struct`, `record struct`) so the convention is locked in.

**Tradeoff / risks.**

- **Mutability surprise on plain `struct`.** A non-`readonly` `struct` can be mutated between the validator running and the handler reading it (defensive-copy rules). Best-practice guidance in the docs: prefer `readonly struct` or `readonly record struct` when pairing with `[Validate]`. Could fire a soft diagnostic (`ZAVAL_???`) when `[Validate]` decorates a non-readonly struct, similar to how Roslyn warns on mutable readonly-context misuse.
- **Generator API surface.** If the generator currently emits `static ClassNameValidator { ... }` with a class-only contract (e.g. takes `T?` nullable reference for the input), struct support needs a small adapter or a duplicate code path. Should be straightforward — pin with the snapshot tests above.

**Graduation signal.** A second `[Validate]`-using codebase requests struct / record-struct support, OR `za-vertical-slice` reaches v1 with `sealed record` as the documented request shape and the team judges the loss of `readonly record struct` ergonomics too high to ship as the convention.

**Relationship to B1.** Independent. B1 unlocks value-object properties; B2 unlocks struct-shaped requests. Same template surfaced both; either can ship first.

</details>
