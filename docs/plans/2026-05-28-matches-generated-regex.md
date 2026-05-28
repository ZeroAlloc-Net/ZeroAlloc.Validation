# `[Matches]` → `[GeneratedRegex]` Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship ZA.Validation 1.5.3 — migrate the generator's `[Matches(pattern)]` emission from static `Regex.IsMatch(input, pattern)` (interpreted, cached lookup, ~70-100 ns/call) to a `[GeneratedRegex(pattern)] private static partial Regex` source-generated method (compile-time-emitted IL, ~3-10 ns/call). Strictly additive perf improvement; existing consumers see no API or behavioral change.

**Architecture:** Thread a `Dictionary<string MethodName, string Pattern>` collector through `RuleEmitter.EmitValidateBody` → `BuildCondition`. The `[Matches]` switch arm appends `(methodName, pattern)` to the collector and returns `!{methodName}().IsMatch({access} ?? "")` as the call site. After `ValidatorGenerator.EmitValidateMethod` emits the Validate body, it appends the `[GeneratedRegex]` partial method declarations at class scope (one per unique methodName). Dedup via Dictionary handles the sync+async double-collection case.

**Tech Stack:** .NET 8/9/10, Roslyn `IIncrementalGenerator`, `[GeneratedRegex]` (System.Text.RegularExpressions source generator, available in .NET 7+).

**Design doc:** `docs/plans/2026-05-28-matches-generated-regex-design.md` (committed at `b57b7f1`).

**Working branch:** `feat/matches-generated-regex` (off `main` at `6412566` — the B4 aot-smoke merge; design committed).

---

## Phase 0 — Orient (5 min)

### Task 0.1: Read the key emitter files + existing test that pins old shape

**Files (read-only):**

- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` lines:
  - 60 — `EmitValidateBody(StringBuilder sb, INamedTypeSymbol classSymbol, string modelParamName = "instance", SourceProductionContext? ctx = null)` — public entry from `ValidatorGenerator`
  - 300, 439 — two `BuildCondition` call sites (sync + async paths through the same emitter)
  - 656 — `BuildCondition(string fqn, AttributeData attr, string access, string propTypeFullName = "", string modelParamName = "instance", ITypeSymbol? propType = null, string? rawAccess = null)` — the switch expression
  - 679 — the `MatchesFqn` arm (the line being changed)
  - 977 — `EmitValidateBodyAsString` — internal helper used by `ValidatorGenerator` for the pipeline-wrapped paths
- `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` lines:
  - 147 — `Validate({modelName} instance)` method signature
  - 151 — direct `EmitValidateBody` call (sync path with no pipeline behaviors)
  - 166, 201 — `EmitValidateBodyAsString` calls (sync + async pipeline behavior paths)
  - 282 — `public sealed partial class {validatorName} : ValidatorFor<{modelName}>` — confirms the class is already partial; `[GeneratedRegex]` partial methods can be added directly
- `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs` line 1102 — `Assert.Contains("Regex.IsMatch", generated, ...)` — the snapshot assertion that needs updating to match the new emission

---

## Phase 1 — Add the regex collector + plumb it through (25 min, 4 tasks)

### Task 1.1: Add the collector parameter to `EmitValidateBody`, `EmitValidateBodyAsString`, and `BuildCondition`

**File (MOD):** `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

Change signatures:

```csharp
// Line 60 — add regexMethods parameter (default null for back-compat, but we'll pass it from all callers)
public static void EmitValidateBody(
    StringBuilder sb,
    INamedTypeSymbol classSymbol,
    string modelParamName = "instance",
    SourceProductionContext? ctx = null,
    System.Collections.Generic.Dictionary<string, string>? regexMethods = null)
{
    // ... body unchanged ...
}

// Line 977 — same parameter
internal static string EmitValidateBodyAsString(
    INamedTypeSymbol classSymbol,
    string modelParamName,
    SourceProductionContext? ctx = null,
    System.Collections.Generic.Dictionary<string, string>? regexMethods = null)
{
    var sb = new StringBuilder();
    EmitValidateBody(sb, classSymbol, modelParamName, ctx, regexMethods);
    return sb.ToString();
}

// Line 656 — propName + regexMethods
private static string BuildCondition(
    string fqn,
    AttributeData attr,
    string access,
    string propTypeFullName = "",
    string modelParamName = "instance",
    ITypeSymbol? propType = null,
    string? rawAccess = null,
    string propName = "",
    System.Collections.Generic.Dictionary<string, string>? regexMethods = null)
{
    // ... switch unchanged for now, the MatchesFqn arm gets updated in Phase 2 ...
}
```

At the two `BuildCondition` call sites (lines 300 + 439), add `prop.Name` and `regexMethods` to the arg list. `prop` is the `IPropertySymbol` in scope at both call sites.

### Task 1.2: Build to verify the signature change compiles cleanly

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet build src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj -c Release 2>&1 | tail -5
```

Expected: 0 errors. The default-null parameters keep all existing callers compiling without changes — they'll just pass nulls until Phase 3.

### Task 1.3: Initialise the collector in `ValidatorGenerator.EmitValidateMethod` + thread through

**File (MOD):** `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs`

Inside `EmitValidateMethod` (around line 140), before the if-else that calls `EmitValidateBody`/`EmitValidateBodyAsString`, declare:

```csharp
var regexMethods = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);
```

Then pass `regexMethods` to all three call sites:

- Line 151: `RuleEmitter.EmitValidateBody(sb, classSymbol, "instance", ctx, regexMethods);`
- Line 166: `RuleEmitter.EmitValidateBodyAsString(classSymbol, paramName, capturedCtx, regexMethods)` (inside the lambda)
- Line 201: `RuleEmitter.EmitValidateBodyAsString(classSymbol, paramName, regexMethods: regexMethods)` (the ValidateAsync path — needs the third positional or named arg)

After the if-else block in `EmitValidateMethod`, return `regexMethods` to the caller (or accept a collector as an out parameter). Simplest: change `EmitValidateMethod`'s signature to return the dictionary, then the caller (`SerializerGenerator.Initialize`'s `RegisterSourceOutput` callback, or wherever this method is called from in `ValidatorGenerator`) appends the partial methods after.

Actually simpler shape: `EmitValidateMethod` itself can emit the partial methods at the end of its own body, BEFORE returning. The method already controls the `sb` writes inside the class body — it can append `[GeneratedRegex]` declarations to `sb` after the Validate method body ends. Phase 3 implements this.

For Phase 1, leave the dictionary populated but unused; Phase 3 adds the emission.

### Task 1.4: Build + commit Phase 1

```bash
dotnet build -c Release 2>&1 | tail -5
```

Expected: 0 errors. Tests can still pass (the dictionary is empty since the `MatchesFqn` arm hasn't been updated yet).

```bash
dotnet test -c Release --filter "FullyQualifiedName!~Integration" 2>&1 | tail -5
```

Expected: full suite green (no behavioral change yet).

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs \
        src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs
git commit -m "refactor(generator): plumb regexMethods collector through Validate emission

Adds an optional Dictionary<string MethodName, string Pattern> parameter to
EmitValidateBody / EmitValidateBodyAsString / BuildCondition. Initialised
in ValidatorGenerator.EmitValidateMethod and passed through to all rule
emission. Currently always empty (the [Matches] arm doesn't populate it
yet) — Phase 2 wires the population; Phase 3 emits [GeneratedRegex]
partial methods from the collected entries."
```

---

## Phase 2 — Update the `[Matches]` switch arm (15 min, 3 tasks)

### Task 2.1: Refactor the `MatchesFqn` arm to populate the collector + return method call

**File (MOD):** `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

The switch expression at line 679 (the `MatchesFqn` arm) currently:

```csharp
MatchesFqn => $"!global::System.Text.RegularExpressions.Regex.IsMatch({access} ?? \"\", \"{EscapeString(GetStringArg(attr, 0))}\")",
```

Change to call a helper method:

```csharp
MatchesFqn => BuildMatchesCondition(access, propName, attr, regexMethods),
```

Add the helper as a private static method on `RuleEmitter` (place it near the other condition-builders, e.g. after `BuildCondition`):

```csharp
private static string BuildMatchesCondition(
    string access,
    string propName,
    AttributeData attr,
    System.Collections.Generic.Dictionary<string, string>? regexMethods)
{
    var methodName = $"__Regex_{propName}";
    var pattern = GetStringArg(attr, 0);

    // If we have a collector, register the pattern so the emitting class
    // can produce the [GeneratedRegex] partial method declaration.
    // Dictionary deduplicates the sync+async double-visit naturally.
    if (regexMethods is not null)
    {
        regexMethods[methodName] = pattern;
    }

    return $"!{methodName}().IsMatch({access} ?? \"\")";
}
```

### Task 2.2: Build + run integration tests to confirm runtime behavior is preserved

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet build -c Release 2>&1 | tail -5
```

Expected: 0 errors.

The build will FAIL the consumer projects that consume the generator (because the generated validators will reference `__Regex_<PropName>()` which has no partial method declaration yet). To verify just the generator, build only the generator project:

```bash
dotnet build src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj -c Release 2>&1 | tail -5
```

Expected: generator builds clean. Downstream consumer compile errors are expected and will be fixed by Phase 3.

DO NOT run the full test suite yet — most generator-consuming tests will fail to compile until Phase 3 emits the partial methods.

### Task 2.3: Commit Phase 2

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs
git commit -m "refactor(generator): [Matches] arm now emits __Regex_<Prop>() call + populates collector

The MatchesFqn switch arm calls a new BuildMatchesCondition helper that:
  - Generates a __Regex_<PropName> method name
  - Adds (methodName, pattern) to the regexMethods collector
  - Returns the new call-site expression: !__Regex_<PropName>().IsMatch(...)

Downstream consumer compilation will fail until Phase 3 emits the
[GeneratedRegex] partial method declarations at validator class scope."
```

---

## Phase 3 — Emit `[GeneratedRegex]` partial methods at class scope (25 min, 4 tasks)

### Task 3.1: After `EmitValidateMethod` emits the Validate body, append the partial method declarations

**File (MOD):** `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs`

Find `EmitValidateMethod` (around line 140). After the if-else block that calls `EmitValidateBody` / `EmitValidateBodyAsString`, after the closing `}` of the `Validate` method, append:

```csharp
// 1.5.3: emit [GeneratedRegex] partial methods for every [Matches] use
// collected during rule emission. One method per (uniquely-named) property.
// Dictionary key dedupes the sync+async double-visit naturally.
foreach (var kvp in regexMethods)
{
    var methodName = kvp.Key;
    var pattern = kvp.Value;
    sb.AppendLine();
    sb.AppendLine($"    [global::System.Text.RegularExpressions.GeneratedRegex(\"{EscapeForRegexAttribute(pattern)}\")]");
    sb.AppendLine($"    private static partial global::System.Text.RegularExpressions.Regex {methodName}();");
}
```

Add a helper method `EscapeForRegexAttribute(string pattern)` next to other helpers on `ValidatorGenerator` (or reuse one if it exists in `RuleEmitter` — check for an existing `EscapeString` method first):

```csharp
private static string EscapeForRegexAttribute(string pattern)
{
    // The pattern string goes inside C# string literal quotes in the emitted
    // [GeneratedRegex(pattern)] attribute argument. Escape backslashes and
    // double quotes; other characters (like \d in the pattern) are already
    // single-character sequences in the C# source — the generator's caller
    // owns whether to use verbatim @"..." vs "...", but the [Matches]
    // attribute pattern is captured verbatim from the user's source, so we
    // only need to escape the C#-string-literal-relevant characters.
    return pattern.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
```

Check if `RuleEmitter.EscapeString` (called from the original Matches arm) exists and reuse if compatible. The original code called `EscapeString(GetStringArg(attr, 0))` — that helper's logic is the same.

### Task 3.2: Build + verify the generator emits both pieces

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet build -c Release 2>&1 | tail -5
```

Expected: build succeeds. Downstream consumer projects (the test project's `Integration/MatchesModel.cs`) now compile because the generator emits the partial method declarations the call sites reference.

If the build fails with `CS8795: Partial method ... must have an implementation part because it has accessibility modifiers`:
- The .NET source generator for `[GeneratedRegex]` provides the implementation. Verify the project has `<LangVersion>` ≥ C# 11 (which is the default for .NET 8+).
- Verify the test project targets .NET 7+ (the `[GeneratedRegex]` runtime requirement).

### Task 3.3: Inspect the generated source to verify shape

After the build, check what the generator actually emitted for the test project's `MatchesModel`:

```bash
find tests/ZeroAlloc.Validation.Tests/obj/Debug -name "MatchesModelValidator.g.cs" 2>&1 | head -3
```

Or for Release:

```bash
find tests/ZeroAlloc.Validation.Tests/obj/Release -name "MatchesModelValidator.g.cs" 2>&1 | head -3
```

Read the file. Confirm it contains:
- A call to `__Regex_<PropName>().IsMatch(...)` inside `Validate`
- A `[GeneratedRegex("...")] private static partial Regex __Regex_<PropName>();` declaration at class scope

If the shape is correct, proceed. If not, surface to user with the actual emitted source as diagnostic.

### Task 3.4: Run integration tests + commit Phase 3

```bash
dotnet test -c Release --filter "FullyQualifiedName~MatchesTests" 2>&1 | tail -5
```

Expected: all integration `MatchesTests` pass — the runtime regex behavior is unchanged.

```bash
git add src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs
git commit -m "feat(generator): emit [GeneratedRegex] partial methods for [Matches]

After EmitValidateMethod emits the Validate body, iterate the regexMethods
collector and append one [GeneratedRegex(pattern)] private static partial
Regex methodName(); declaration per unique (methodName, pattern) entry.

Combined with Phase 2's call-site change, this completes the [Matches] →
source-gen regex migration. Per-call cost drops from ~70-100 ns
(interpreted Regex.IsMatch + cache lookup) to ~3-10 ns (compile-time-
emitted matcher IL, direct dispatch). Strictly additive — no API change."
```

---

## Phase 4 — Update the snapshot test that pins old emission (10 min, 3 tasks)

### Task 4.1: Update the existing `Regex.IsMatch` assertion

**File (MOD):** `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

Read line 1099-1110 to see the existing test that pins `Assert.Contains("Regex.IsMatch", generated, StringComparison.Ordinal);`.

Replace the assertion with the new emission shape:

```csharp
Assert.Contains("__Regex_Zip()", generated, StringComparison.Ordinal);
Assert.Contains("[global::System.Text.RegularExpressions.GeneratedRegex", generated, StringComparison.Ordinal);
Assert.Contains("private static partial global::System.Text.RegularExpressions.Regex __Regex_Zip();", generated, StringComparison.Ordinal);
Assert.DoesNotContain("Regex.IsMatch(", generated, StringComparison.Ordinal);
```

The `DoesNotContain` assertion explicitly rejects the old emission shape — if a future regression reverts to inline `Regex.IsMatch`, this test fails.

### Task 4.2: Run + verify the test passes

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet test -c Release --filter "FullyQualifiedName~GeneratorRuleEmissionTests" 2>&1 | tail -10
```

Expected: all generator tests pass (including the updated assertion at line 1102).

If the test fails:
- "Substring `__Regex_Zip()` not found" — the call-site emission isn't generating the expected method name. Check that the property name is `Zip` (line 1099 of the test) and that `BuildMatchesCondition` builds the method name correctly from `propName`.
- "Substring `[global::System.Text.RegularExpressions.GeneratedRegex` not found" — the partial method declaration wasn't emitted. Check Phase 3's `EmitValidateMethod` change.

### Task 4.3: Commit Phase 4

```bash
git add tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "test(generator): update [Matches] snapshot assertion for [GeneratedRegex] emission

Replaces the Assert.Contains('Regex.IsMatch', ...) check (pinning the old
inline static-call emission) with three new assertions that pin the new
shape:
  - Call site: __Regex_Zip()
  - Attribute: [global::System.Text.RegularExpressions.GeneratedRegex
  - Partial method: private static partial Regex __Regex_Zip();

Plus a DoesNotContain('Regex.IsMatch(') assertion that catches a
regression to the old inline-static-call shape."
```

---

## Phase 5 — New snapshot test for dual emission (10 min, 3 tasks)

### Task 5.1: Add a focused snapshot test asserting both pieces

**File (MOD):** `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

Add a new `[Fact]` method (place it near the existing `[Matches]` test around line 1099):

```csharp
[Fact]
public void Matches_Emits_GeneratedRegex_PartialMethod_AndCallSite()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public sealed class WithMatches
        {
            [NotEmpty]
            [Matches(@"^[0-9]{4}[A-Z]{2}$")]
            public string ShippingZip { get; set; } = "";
        }
        """;

    var compilation = CreateCompilation(source);
    var generator = new ValidatorGenerator();
    var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
    var generated = driver.GetRunResult().GeneratedTrees.FirstOrDefault(t =>
        t.FilePath.EndsWith("WithMatchesValidator.g.cs", StringComparison.Ordinal))?.ToString();

    Assert.NotNull(generated);

    // Call site uses the generated method
    Assert.Contains("__Regex_ShippingZip().IsMatch(", generated!, StringComparison.Ordinal);

    // Partial method declaration is emitted at class scope
    Assert.Contains("[global::System.Text.RegularExpressions.GeneratedRegex(\"^[0-9]{4}[A-Z]{2}$\")]", generated, StringComparison.Ordinal);
    Assert.Contains("private static partial global::System.Text.RegularExpressions.Regex __Regex_ShippingZip();", generated, StringComparison.Ordinal);

    // Old emission shape is gone
    Assert.DoesNotContain("System.Text.RegularExpressions.Regex.IsMatch(", generated, StringComparison.Ordinal);
}
```

The `CreateCompilation` helper is already in the test file — reuse the existing convention.

### Task 5.2: Run + verify

```bash
dotnet test -c Release --filter "FullyQualifiedName~Matches_Emits_GeneratedRegex_PartialMethod_AndCallSite" 2>&1 | tail -5
```

Expected: 1/1 pass.

### Task 5.3: Run the full suite + commit

```bash
dotnet test -c Release 2>&1 | tail -5
```

Expected: full suite green — all existing tests still pass, plus the new one.

```bash
git add tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "test(generator): explicit snapshot test for [Matches] dual emission

Asserts the call-site (__Regex_ShippingZip().IsMatch(...)) AND the
[GeneratedRegex] partial method declaration at class scope. Includes
a DoesNotContain check against the old Regex.IsMatch shape so a
regression reverting the change fails this test clearly."
```

---

## Phase 6 — Push + PR + ship (15 min, 2 tasks)

### Task 6.1: Push + open PR

```bash
git push -u origin feat/matches-generated-regex

gh pr create \
  --title "perf(generator): emit GeneratedRegex partial method for Matches" \
  --body "$(cat <<'EOF'
## Summary

Ships ZA.Validation 1.5.3. Migrates the generator's `[Matches(pattern)]` emission from static `Regex.IsMatch(input, pattern)` (interpreted, per-call cached lookup, ~70-100 ns) to a `[GeneratedRegex(pattern)] private static partial Regex` source-generated method (compile-time-emitted IL, ~3-10 ns).

Strictly additive perf improvement. No API or behavioral changes for consumers. Generated validators recompile cleanly to the new shape on package upgrade.

## Why now

Surfaced 2026-05-28 during the post-typed-ID-migration benchmark refresh in [ZeroAlloc.Templates PR #133](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/133). User asked "why is the validator benchmark slower (40 → 112 ns)?" — root cause was the static `Regex.IsMatch` per-call cost on the `[Matches]` rule, not the typed-ID migration or any generator semantic change.

## What changed

- **`RuleEmitter.cs`** — `[Matches]` switch arm now calls `BuildMatchesCondition` helper which:
  - Generates a `__Regex_<PropName>` method name from the property
  - Adds `(methodName, pattern)` to a `Dictionary<string, string>` collector
  - Returns the new call-site expression `!__Regex_<PropName>().IsMatch(...)`
- **`ValidatorGenerator.cs`** — After emitting the `Validate` body, iterates the collector and appends one `[GeneratedRegex(pattern)] private static partial Regex __Regex_<PropName>();` declaration per entry at class scope
- **Plumbing** — Added optional `Dictionary<string, string>? regexMethods` parameter to `EmitValidateBody`, `EmitValidateBodyAsString`, `BuildCondition`. Threaded from `ValidatorGenerator.EmitValidateMethod`.
- **Dedup** — Dictionary key (`methodName`) deduplicates the sync+async double-visit. One partial method per unique property name.
- **Tests** — Updated existing `[Matches]` snapshot assertion to pin the new shape (with `DoesNotContain('Regex.IsMatch(')` to catch reversions); added a focused new snapshot test for dual emission (call-site + partial-method declaration).

## Expected perf impact

The `Validator_Generated` benchmark in `ZeroAlloc.Templates`'s za-clean `PrimitivesBench` (currently 112 ns/call) should drop to ~40-50 ns/call on the same hardware after consumers upgrade to 1.5.3.

## Decisions ([design doc](docs/plans/2026-05-28-matches-generated-regex-design.md))

- **`[GeneratedRegex]` source-gen, not compiled-static-field** — perf-optimal, AOT-friendly, aligns with .NET 8+ codegen direction
- **One partial method per property, no pattern-string dedup across properties** — Dictionary keyed by methodName handles sync+async dedup; cross-property pattern dedup is YAGNI
- **Optional Dictionary param** — back-compatible signature; existing callers pass null until Phase 3 wires the population

## SemVer

`1.5.2` → `1.5.3` (patch — strictly additive perf improvement). Conventional commit: `perf(generator): ...`.

## Test plan

- [x] All existing generator tests pass with the updated assertion
- [x] All integration `MatchesTests` pass (runtime behavior unchanged)
- [x] New snapshot test pins the dual-emission shape
- [ ] CI green on this PR
- [ ] Follow-up after 1.5.3 propagates: ZA.Templates re-runs the benchmark workflow + observes the Validator_Generated drop

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

### Task 6.2: STOP after PR opens

Do NOT admin-merge. Wait for CI to verify; user handles the merge.

---

## Verification checklist

- [ ] **Phase 1:** Signature plumbing in place; full build clean; existing tests still pass
- [ ] **Phase 2:** `[Matches]` arm populates the dictionary; emits `__Regex_<PropName>()` call-site
- [ ] **Phase 3:** `EmitValidateMethod` appends `[GeneratedRegex]` partial method declarations at class scope; consumers compile cleanly
- [ ] **Phase 4:** Existing snapshot test updated to assert new shape + reject old shape
- [ ] **Phase 5:** New focused snapshot test pins dual emission
- [ ] **Phase 6:** PR opened, CI green, awaiting merge

## Out of scope (deferred to backlog)

- **Pattern-string deduplication across properties** — multiple `[Matches("foo")]` uses on different properties of the same model emit duplicate partial methods. YAGNI.
- **`RegexOptions` support on `[Matches]`** — the attribute currently only takes a pattern. Adding options (`IgnoreCase`, `Multiline`, `RegexOptions` flags, etc.) is a separate API extension.
- **Regex benchmark in ZA.Validation itself** — load-bearing measurement is in `ZeroAlloc.Templates`. Defer.
- **Apply same source-gen pattern to other patterns** — `[EmailAddress]` uses a built-in regex; could similarly migrate. Defer until measured cost matters.
