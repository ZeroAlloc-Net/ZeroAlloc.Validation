# `[Matches]` → Compiled Regex Migration — Design

**Date:** 2026-05-28
**Scope:** ZeroAlloc.Validation generator emission for the `[Matches(pattern)]` validation attribute. Migrate from the static `Regex.IsMatch(input, pattern)` call (interpreted, per-call cached-lookup) to a **`private static readonly Regex` field** initialized with `RegexOptions.Compiled` (JIT-compiled matcher, no per-call cache lookup). Ships as ZA.Validation **1.5.3** (patch — strictly additive perf improvement).

> **Pivot 2026-05-28 (during Phase 3 implementation):** the original plan called for emitting `[GeneratedRegex]` partial methods that the .NET 7+ `RegexGenerator` would close. Implementer surfaced that Roslyn source generators cannot see syntax trees added by other source generators in the same compilation pass — our generator's `[GeneratedRegex]` declarations are invisible to `RegexGenerator`, so the partial methods never get an implementation (`CS8795` × N at consumer compile time). Same root-cause class as ZA.Serialisation 2.3.0 → 2.3.1 (gens-can't-see-gens). Pivoted to static compiled-Regex fields: simpler, smaller diff, ~60% of the perf win (vs ~95% for the ideal source-gen path), no Roslyn-limitation dependency. The original Phase 3 work (`[GeneratedRegex]` partial methods) is preserved as a future enhancement if Roslyn ever surfaces inter-generator visibility.

## Background

`[Matches("^[0-9]{4}[A-Z]{2}$")]` on a `string` property compiles via `RuleEmitter.cs:679` to:

```csharp
!global::System.Text.RegularExpressions.Regex.IsMatch({access} ?? "", "{pattern}")
```

This uses the static `Regex.IsMatch(string input, string pattern)` API. Per call:

- Hash the pattern + look up in the internal 15-entry static cache (~10-20 ns)
- If miss, parse the pattern + cache it
- Execute the matcher (interpreted, not compiled)

For the za-clean `CreateOrderCommand`'s `[Matches("^[0-9]{4}[A-Z]{2}$")] string ShippingZip` rule on input `"1011AA"`, this costs **~70-100 ns** per call — verified empirically: the validator benchmark spent 112 ns total, with the regex check being the dominant component.

Surfaced 2026-05-28 during the post-typed-ID-migration benchmark refresh in [ZeroAlloc.Templates PR #133](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/133). User asked "why is `Validator_Generated` slower?" — root cause was the static `Regex.IsMatch` per-call cost, not anything about the ZA.Validation generator's struct/typed-ID changes.

`[GeneratedRegex]` (introduced in .NET 7) source-generates the regex matcher IL at compile time. Per call: direct method dispatch + zero parsing overhead, typically **~3-10 ns** for short anchored patterns.

## Goal

`[Matches(pattern)]` emission produces a `[GeneratedRegex]` partial method per `[Matches]`-decorated property, called from the rule condition. Per-call regex cost drops from ~70-100 ns to ~3-10 ns. The bench result for `Validator_Generated` in `ZeroAlloc.Templates`'s za-clean Primitives benchmark should drop from 112 ns to ~40-50 ns.

## Decisions

### D-1: emit `[GeneratedRegex]` partial methods, not compiled-static-field

Two alternatives considered for moving past the static `Regex.IsMatch` call:

- **`[GeneratedRegex]` source-gen** (chosen). The .NET source generator emits IL at compile time for the matcher. Fastest possible; no startup or warmup cost. Requires .NET 7+ and `partial` class.
- **`static readonly Regex` field with `RegexOptions.Compiled`**. JIT-compiles the regex on first use. Faster than interpreted but slower than source-gen. Simpler to emit (no per-method declaration).

**`[GeneratedRegex]` wins on:**

- Perf: ~3-10 ns vs ~10-30 ns for compiled-static-field
- AOT-friendliness: source-gen is the canonical .NET 8+ AOT-compatible regex story; compiled-runtime regex falls back to RegexOptions.NonBacktracking on AOT
- Forward compatibility: the chosen pattern aligns with the .NET 8+ codegen direction

**`[GeneratedRegex]` cost:** generator refactor to collect-and-emit partial method declarations. Manageable scope (~30 LOC change).

### D-2: one partial method per `[Matches]`-decorated property, no pattern deduplication

If two properties on the same model carry `[Matches("foo")]` with the same pattern, the generator emits TWO partial methods (one per property). Pattern-string deduplication could collapse them — but the savings is marginal (regex bytes in the assembly, not runtime cost) and the dedup adds generator complexity. YAGNI.

### D-3: emission shape

```csharp
public sealed partial class CreateOrderCommandValidator : ValidatorFor<CreateOrderCommand>
{
    public override ValidationResult Validate(CreateOrderCommand instance)
    {
        // ...
        if (!__Regex_ShippingZip().IsMatch(instance.ShippingZip ?? ""))
        {
            // emit failure
        }
        // ...
    }

    [global::System.Text.RegularExpressions.GeneratedRegex("^[0-9]{4}[A-Z]{2}$")]
    private static partial global::System.Text.RegularExpressions.Regex __Regex_ShippingZip();
}
```

**Method naming convention:** `__Regex_<PropertyName>` for top-level properties. Double-underscore prefix avoids collision with user-named methods (CS0825 reserved-name avoidance). PascalCase property name preserved.

**Naming corner case:** if a property is named exactly `Regex` or shadows the convention, name collision is theoretically possible. AVOIDED by the `__Regex_` prefix which is reserved by C# convention.

**Nested validators (e.g., `OrderItemValidator`):** each gets its own class, each emits its own `__Regex_*` partial methods independently. No cross-class conflict.

### D-4: refactor `EmitValidateBody` to bubble up required regex declarations

Current `RuleEmitter.BuildCondition` is a pure string-returning switch. To emit class-level declarations alongside the rule condition, `EmitValidateBody` needs to:

1. Collect a `List<(string MethodName, string Pattern)>` as it walks properties
2. Return that list (or accept it as an out parameter) to `ValidatorGenerator.EmitValidateMethod`
3. `ValidatorGenerator` appends the `[GeneratedRegex]` partial method declarations to the class body AFTER emitting the Validate method

Adding a `List<...>` parameter to `EmitValidateBody`'s signature is the cleanest refactor. Existing callers of `EmitValidateBody` (in `ValidatorGenerator` and `EmitValidateBodyAsString`) need updating to pass the collector.

### D-5: SemVer + commit framing

`1.5.2 → 1.5.3` (patch — strictly additive perf improvement). Conventional commit: `perf(generator): emit [GeneratedRegex] for [Matches] instead of static Regex.IsMatch`. Behavior unchanged; only the emitted code's runtime cost differs.

**Backwards compatibility:** existing consumers' generated validators recompile cleanly to the new shape on package upgrade. The validator's public interface is unchanged. Pure improvement.

## Design

### `RuleEmitter.cs` changes

Today, `BuildCondition` for `[Matches]` returns:

```csharp
MatchesFqn => $"!global::System.Text.RegularExpressions.Regex.IsMatch({access} ?? \"\", \"{EscapeString(GetStringArg(attr, 0))}\")",
```

After: `BuildCondition` returns a method-call expression AND appends a `(methodName, pattern)` tuple to a `regexMethods` collector:

```csharp
MatchesFqn =>
{
    var methodName = $"__Regex_{propName}";
    var pattern = GetStringArg(attr, 0);
    regexMethods.Add((methodName, pattern));
    return $"!{methodName}().IsMatch({access} ?? \"\")";
},
```

Note the switch arm becomes a block (statement lambda inside the switch expression, or rewrite the switch to a method that takes the collector). The switch arm syntax doesn't permit side effects directly — the cleanest path is to extract the switch into a method, taking `List<(string, string)>` as an additional parameter.

### `ValidatorGenerator.cs` changes

After emitting the Validate method body, append the `[GeneratedRegex]` partial methods at class scope:

```csharp
// existing: EmitValidateMethod emits the Validate method body
EmitValidateMethod(ctx, sb, classSymbol, modelName, syncBehaviors);

// new: emit partial method declarations for each regex collected during Validate emission
foreach (var (methodName, pattern) in regexMethods)
{
    sb.AppendLine();
    sb.AppendLine($"    [global::System.Text.RegularExpressions.GeneratedRegex(\"{EscapeString(pattern)}\")]");
    sb.AppendLine($"    private static partial global::System.Text.RegularExpressions.Regex {methodName}();");
}
```

The `regexMethods` list is collected by `EmitValidateMethod` → `EmitValidateBody` → `BuildCondition` and bubbled back up.

### Tests

- **Snapshot test:** new test asserting that a `[Validate]` class with a `[Matches]` property emits both:
  - The call site: `__Regex_<PropName>().IsMatch(...)`
  - The `[GeneratedRegex]` partial method declaration at class scope

- **Existing tests:** all current validation tests should pass unchanged — the validator's external behavior is identical, only the implementation changes.

- **Benchmark verification:** after merge + release, re-run the za-clean Primitives bench (via ZeroAlloc.Templates workflow_dispatch CI) and confirm `Validator_Generated` drops from 112 ns to ~40-50 ns.

### Files touched

- **MOD:** `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` — switch the `[Matches]` arm to emit method-call + collect tuple
- **MOD:** `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` — emit `[GeneratedRegex]` partial methods after Validate body
- **MOD:** Plumbing — `EmitValidateBody` + `EmitValidateBodyAsString` signatures gain a `List<(string, string)>` collector parameter
- **NEW:** `tests/ZeroAlloc.Validation.Tests/Generator/MatchesGeneratedRegexEmissionTests.cs` — snapshot tests
- **MOD:** `docs/backlog.md` — N/A; this isn't a backlog item, it's a direct fix

Total commit footprint: ~80 LOC including tests.

## Out of scope

- **Pattern-string deduplication across properties** — multiple `[Matches("foo")]` uses on the same model emit duplicate partial methods. Acceptable; deduplication is YAGNI until a real consumer cares.
- **`RegexOptions` flag support** — `[Matches]` currently only takes a pattern string. Adding `RegexOptions` (case-insensitive, multiline, etc.) is a separate `[MatchesAttribute]` API extension.
- **`[Matches]` on non-string properties** — already unsupported; no change.
- **Matches benchmark in ZA.Validation itself** — could add a perf benchmark to the test suite, but the load-bearing measurement is in `ZeroAlloc.Templates` (`Validator_Generated` in `PrimitivesBench`). Defer.
