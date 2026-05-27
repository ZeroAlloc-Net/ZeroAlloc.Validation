# Nested-Validator Value-Type Null-Guard Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship ZeroAlloc.Validation 1.4.1 — generator skips the `is not null` guard at the two nested-validator emission sites when the type is a non-nullable value type.

**Architecture:** One private helper (`NeedsNullGuard`) wraps the predicate; both emission sites in `RuleEmitter.cs` consult it before emitting the guard. Strictly subtractive at the .g.cs output level — class-typed paths unchanged, struct-typed paths newly compile. Closes backlog B3.

**Tech Stack:** .NET 10, Roslyn incremental generator (`IIncrementalGenerator`), xUnit, `CSharpGeneratorDriver` for snapshot-style tests.

**Design doc:** `docs/plans/2026-05-27-nested-validator-value-type-design.md` (committed at `274fd3b`).

**Working branch:** `fix/nested-validator-value-type` (already created off `main`; design committed).

---

## Phase 0 — Orient (3 min)

Skim three locations before touching code.

### Task 0.1: Read the two buggy emission sites

**Files (read-only):**
- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs:312-324` — `EmitNestedValidatorForProp` (scalar). Line 317 emits the unconditional `is not null`.
- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs:335-356` — `EmitCollectionValidatorForProp`. Line 346 emits the inner-element `is not null`. Line 341 (outer collection-null check) is correct as-is — `IReadOnlyList<T>` is always a reference.

Note the parent `EmitCollectionValidators` already constructs `List<(IPropertySymbol Property, INamedTypeSymbol ElementType)>` (lines 326-333) — the element type is computed upstream and available, but `EmitCollectionValidatorForProp` currently only receives the `Property`. The fix passes the element type through.

### Task 0.2: Read a sibling generator test

**File (read-only):** `tests/ZeroAlloc.Validation.Tests/Generator/StructValidationDiagnosticTests.cs` (the 5-snapshot file shipped in 1.4.0). Same shape we want for the new tests: `CSharpCompilation.Create` + `CSharpGeneratorDriver.RunGenerators` + `result.Diagnostics` / `result.GeneratedTrees` assertions.

For the new tests we also need to inspect the generated **source text** (assert that `is not null` is or isn't present in the output for a specific element). Look at `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs` (any test in that file) for the pattern: `result.GeneratedTrees.Single(t => t.FilePath.EndsWith("XValidator.g.cs")).ToString()` and then `Assert.Contains(...)` / `Assert.DoesNotContain(...)` against that string.

---

## Phase 1 — Failing test for the collection-element case (TDD, 20 min, 4 tasks)

### Task 1.1: Create the test file with the first failing case

**File (NEW):** `tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs`

```csharp
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class NestedValidatorValueTypeTests
{
    [Fact]
    public void Collection_Of_ReadonlyRecordStruct_Compiles_WithoutItemNullGuard()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed record Outer([property: NotEmpty] IReadOnlyList<Item> Items);

            [Validate]
            public readonly record struct Item([property: GreaterThan(0)] int Qty);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var outerValidator = GetGeneratedSource(result, "OuterValidator.g.cs");
        Assert.DoesNotContain("_c0Item is not null", outerValidator, StringComparison.Ordinal);
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult result, string filenameSuffix) =>
        result.GeneratedTrees
            .First(t => t.FilePath.EndsWith(filenameSuffix, StringComparison.Ordinal))
            .ToString();

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
```

If existing tests use `string.Equals(..., StringComparison.Ordinal)` for diagnostic-ID checks (MA0006), the `Assert.DoesNotContain` call already accepts `StringComparison` — no rewrite needed.

If the repo convention requires a `#pragma warning disable MA0048` for multi-type test files, add it at the top (sibling tests in the same directory show whether it's needed).

### Task 1.2: Run — expect FAIL via build error in the test assembly

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet test -c Release --filter "FullyQualifiedName~NestedValidatorValueTypeTests"
```

Expected: the **TestAssembly compilation inside the test** still succeeds at the .NET-build level (the generator-driven test doesn't fail the host build); what fails is the assertion `Assert.Empty(result.Diagnostics)` — Diagnostics will contain a CS0037 emitted by the generated `OuterValidator.g.cs`. Read the error to confirm: `Cannot convert null to 'Item' because it is a non-nullable value type`.

If the test passes instead, something's off — the generator is already handling value types in some other code path, and the bug premise needs re-validation before proceeding.

### Task 1.3: Implement the fix (collection site only)

**File:** `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

**Step 1:** Add the helper. Place it near the bottom of `RuleEmitter` (private static, no caller other than the two emission sites):

```csharp
    /// <summary>
    /// Returns true when an `is not null` guard against a value of this type
    /// would compile and have meaningful runtime semantics. Class types always
    /// need the guard. <c>Nullable&lt;T&gt;</c> keeps it (the guard lowers to
    /// <c>HasValue</c>). Non-nullable value types cannot take the guard (CS0037),
    /// so the generator omits it.
    /// </summary>
    private static bool NeedsNullGuard(ITypeSymbol type) =>
        !type.IsValueType
        || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
```

**Step 2:** Plumb the element type through to `EmitCollectionValidatorForProp`. Current signature:

```csharp
private static void EmitCollectionValidatorForProp(StringBuilder sb, IPropertySymbol collProp, int ci, string modelParamName)
```

Change to:

```csharp
private static void EmitCollectionValidatorForProp(StringBuilder sb, IPropertySymbol collProp, INamedTypeSymbol elementType, int ci, string modelParamName)
```

Update the caller at `EmitCollectionValidators` (around line 331-332):

```csharp
    for (int ci = 0; ci < collectionProperties.Count; ci++)
        EmitCollectionValidatorForProp(sb, collectionProperties[ci].Property, collectionProperties[ci].ElementType, ci, modelParamName);
```

**Step 3:** Conditionally emit the item-null guard inside `EmitCollectionValidatorForProp`. Find the existing block (lines 346-351):

```csharp
        sb.AppendLine($"                if ({varName}Item is not null)");
        sb.AppendLine("                {");
        sb.AppendLine($"                    var {varName}Result = _{camelC}Validator.Validate({varName}Item);");
        sb.AppendLine($"                    foreach (ref readonly var f in {varName}Result.Failures)");
        sb.AppendLine($"                        _buf.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}[\" + {varName}Idx + \"].\" + f.PropertyName, ErrorMessage = f.ErrorMessage, ErrorCode = f.ErrorCode, Severity = f.Severity }});");
        sb.AppendLine("                }");
```

Replace with:

```csharp
        var needsItemGuard = NeedsNullGuard(elementType);
        if (needsItemGuard)
        {
            sb.AppendLine($"                if ({varName}Item is not null)");
            sb.AppendLine("                {");
        }
        sb.AppendLine($"                    var {varName}Result = _{camelC}Validator.Validate({varName}Item);");
        sb.AppendLine($"                    foreach (ref readonly var f in {varName}Result.Failures)");
        sb.AppendLine($"                        _buf.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}[\" + {varName}Idx + \"].\" + f.PropertyName, ErrorMessage = f.ErrorMessage, ErrorCode = f.ErrorCode, Severity = f.Severity }});");
        if (needsItemGuard)
        {
            sb.AppendLine("                }");
        }
```

(Indentation in the .g.cs is harmless — `csc` ignores it; nobody hand-reads generator output.)

### Task 1.4: Run — expect PASS + commit

```bash
dotnet test -c Release --filter "FullyQualifiedName~NestedValidatorValueTypeTests"
```

Expected: 1/1 pass.

Run the full suite:

```bash
dotnet test -c Release
```

Expected: every existing test stays green. The change only affects the value-type branch of the emission; class-typed paths produce byte-identical output.

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs \
        tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs
git commit -m "fix(generator): skip is-not-null on value-type collection elements

When [Validate] contains IReadOnlyList<T> where T is a non-nullable
value type, the generator's foreach emission used to produce
'if (item is not null)' — CS0037 against a non-nullable struct. Now
gated by NeedsNullGuard(elementType): the guard stays for class T and
Nullable<T> (the latter lowers to .HasValue), and is omitted only for
non-nullable value types. Scalar nested-property site (line 317) gets
the same treatment in the next commit."
```

---

## Phase 2 — Add the class regression net + Nullable<T> case (15 min, 3 tasks)

### Task 2.1: Append two more test cases

**File:** `tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs`

Inside the test class, after `Collection_Of_ReadonlyRecordStruct_Compiles_WithoutItemNullGuard`, append:

```csharp
    [Fact]
    public void Collection_Of_Class_Still_Emits_ItemNullGuard()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed record Outer([property: NotEmpty] IReadOnlyList<Item> Items);

            [Validate]
            public sealed record Item([property: GreaterThan(0)] int Qty);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var outerValidator = GetGeneratedSource(result, "OuterValidator.g.cs");
        Assert.Contains("_c0Item is not null", outerValidator, StringComparison.Ordinal);
    }

    [Fact]
    public void Collection_Of_NullableStruct_Still_Emits_ItemNullGuard()
    {
        var source = """
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed record Outer([property: NotEmpty] IReadOnlyList<Item?> Items);

            [Validate]
            public readonly record struct Item([property: GreaterThan(0)] int Qty);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var outerValidator = GetGeneratedSource(result, "OuterValidator.g.cs");
        Assert.Contains("_c0Item is not null", outerValidator, StringComparison.Ordinal);
    }
```

### Task 2.2: Run — expect 3/3 pass

```bash
dotnet test -c Release --filter "FullyQualifiedName~NestedValidatorValueTypeTests"
```

Expected: 3/3 pass. The class case proves the regression net; the `Nullable<Item>` case proves the predicate keeps the guard for `Nullable<T>` (where `is not null` lowers to `.HasValue` and is the correct runtime behaviour).

If the `Nullable<Item>` case fails — i.e. `_c0Item is not null` is absent from the generated source — the predicate's `Nullable<T>` branch is wrong. Re-check that `type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T` is the right discriminator (likely yes; possibly also check `type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }`).

### Task 2.3: Commit

```bash
git add tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs
git commit -m "test(generator): class + Nullable<T> regression net for collection-element guard"
```

---

## Phase 3 — Scalar nested-property site (TDD, 20 min, 4 tasks)

### Task 3.1: Add the failing scalar-property case

**File:** `tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs`

Append:

```csharp
    [Fact]
    public void Scalar_NestedValidator_Of_ReadonlyRecordStruct_Compiles_WithoutNullGuard()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed record Outer(
                [property: ValidateWith(typeof(InnerValidator))] Inner Inner);

            [Validate]
            public readonly record struct Inner([property: GreaterThan(0)] int Qty);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var outerValidator = GetGeneratedSource(result, "OuterValidator.g.cs");
        Assert.DoesNotContain("instance.Inner is not null", outerValidator, StringComparison.Ordinal);
    }
```

The `ValidateWith` reference resolves because the test source imports `ZeroAlloc.Validation` (where `[ValidateWith]` lives). The generator's nested-validator discovery picks up the scalar property via `[ValidateWith]`.

`InnerValidator` is the type that the generator emits for `[Validate]`-decorated `Inner`; the `typeof(InnerValidator)` parses as long as the generator runs first to make the type visible (incremental driver makes this work for snapshot tests — see how `GeneratorRuleEmissionTests` handles similar cases).

### Task 3.2: Run — expect FAIL with the CS0037 in the generator's output diagnostics

```bash
dotnet test -c Release --filter "Scalar_NestedValidator_Of_ReadonlyRecordStruct"
```

Expected: assertion fails because `result.Diagnostics` contains a `CS0037` against `instance.Inner is not null`. Confirm before moving on — different failure modes (e.g. `InnerValidator` not found) indicate a setup issue with the test, not the production bug.

### Task 3.3: Apply the same guard to the scalar site

**File:** `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

Find `EmitNestedValidatorForProp` (lines 312-324). Current body:

```csharp
        sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var nestedResult = _{camelN}Validator.Validate({modelParamName}.{propName});");
        sb.AppendLine("            foreach (ref readonly var f in nestedResult.Failures)");
        sb.AppendLine($"                _buf.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}.\" + f.PropertyName, ErrorMessage = f.ErrorMessage, ErrorCode = f.ErrorCode, Severity = f.Severity }});");
        sb.AppendLine("        }");
```

Replace with:

```csharp
        var needsPropGuard = NeedsNullGuard(nestedProp.Type);
        if (needsPropGuard)
        {
            sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
            sb.AppendLine("        {");
        }
        sb.AppendLine($"            var nestedResult = _{camelN}Validator.Validate({modelParamName}.{propName});");
        sb.AppendLine("            foreach (ref readonly var f in nestedResult.Failures)");
        sb.AppendLine($"                _buf.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}.\" + f.PropertyName, ErrorMessage = f.ErrorMessage, ErrorCode = f.ErrorCode, Severity = f.Severity }});");
        if (needsPropGuard)
        {
            sb.AppendLine("        }");
        }
```

### Task 3.4: Run — expect 4/4 pass, then commit

```bash
dotnet test -c Release --filter "FullyQualifiedName~NestedValidatorValueTypeTests"
```

Expected: 4/4 pass.

```bash
dotnet test -c Release
```

Expected: full suite still green.

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs \
        tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs
git commit -m "fix(generator): skip is-not-null on value-type scalar nested validator

[ValidateWith(typeof(InnerValidator))] InnerStruct Inner used to emit
'if (instance.Inner is not null)' — CS0037 against a non-nullable
struct. Same NeedsNullGuard predicate as the collection-element fix
gates the emission. Class T and Nullable<T> still emit the guard."
```

---

## Phase 4 — Scalar class regression net (5 min, 2 tasks)

### Task 4.1: Append the regression case

**File:** `tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs`

```csharp
    [Fact]
    public void Scalar_NestedValidator_Of_Class_Still_Emits_NullGuard()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed record Outer(
                [property: ValidateWith(typeof(InnerValidator))] Inner Inner);

            [Validate]
            public sealed record Inner([property: GreaterThan(0)] int Qty);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var outerValidator = GetGeneratedSource(result, "OuterValidator.g.cs");
        Assert.Contains("instance.Inner is not null", outerValidator, StringComparison.Ordinal);
    }
```

### Task 4.2: Run + commit

```bash
dotnet test -c Release --filter "FullyQualifiedName~NestedValidatorValueTypeTests"
```

Expected: 5/5 pass.

```bash
git add tests/ZeroAlloc.Validation.Tests/Generator/NestedValidatorValueTypeTests.cs
git commit -m "test(generator): class regression net for scalar nested validator guard"
```

---

## Phase 5 — Backlog + ship (15 min, 3 tasks)

### Task 5.1: Add B3 to `docs/backlog.md`

**File:** `docs/backlog.md`

Insert above B1 (so it reads B3, B1, ~~B2~~) — same `## B3 — …` heading shape as B1's existing `## B1 — …`:

```markdown
## B3 — Nested-validator emits `is not null` against value-type elements

**What.** When a `[Validate]` type contains `IReadOnlyList<T>` (where T is `[Validate]`-decorated) or a `[ValidateWith(...)]` scalar property, the generator emits `if (... is not null)` unconditionally. Class T and `Nullable<T>` compile correctly; **non-nullable value types fail with `CS0037`** (cannot convert null to a non-nullable value type).

**Why.** Surfaced 2026-05-27 while migrating `za-clean`'s `CreateOrderCommand` + `OrderItem` from `sealed record` to `readonly record struct` ([ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates) follow-up to the 1.4.0 ship). `CreateOrderCommand` migrates fine; `OrderItem` (inside `IReadOnlyList<OrderItem> Items`) trips the generator. Same code path emits the same guard for scalar `[ValidateWith]` nested properties — latent bug there too.

**Sketch.** Predicate `NeedsNullGuard(ITypeSymbol)` returning `false` only for non-nullable value types. Applied at both nested emission sites in `RuleEmitter.cs`. Class types and `Nullable<T>` keep the guard (the `Nullable<T>` case lowers to `.HasValue` and is correct).

**Tradeoff / risks.**

- Indentation drift in the generated `.g.cs` when the guard is omitted (no harm — `csc` ignores indentation; nobody hand-reads generator output).
- Public API surface unchanged; pure subtractive fix at the generator-output level.

**Graduation signal.** Same template surfaced the bug; landing it is the graduation. Ships as **1.4.1** (patch).
```

### Task 5.2: Run the full suite + push the branch + open the PR

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet test -c Release
```

Expected: every test passes — the 5 new B3 tests + every existing test from before this branch.

```bash
git add docs/backlog.md
git commit -m "docs(backlog): file B3 — nested-validator value-type null-guard"
git push -u origin fix/nested-validator-value-type
```

```bash
gh pr create \
  --title "fix(generator): skip is-not-null on value-type nested validators" \
  --body "$(cat <<'EOF'
## Summary

Closes backlog B3. ZA.Validation's generator emitted ``if (... is not null)`` unconditionally at two nested-validator emission sites in ``RuleEmitter.cs`` — line 317 (scalar ``[ValidateWith]`` property) and line 346 (foreach element of ``IReadOnlyList<T>``). Class T and ``Nullable<T>`` compile; **non-nullable value type T fails with CS0037**.

Surfaced 2026-05-27 attempting to migrate ``za-clean``'s ``OrderItem`` from ``sealed record`` to ``readonly record struct`` inside ``CreateOrderCommand.Items``. Same emission path applies to scalar ``[ValidateWith]`` properties (line 317) — latent, no current consumer, fixed in the same commit because it's the same predicate.

## What changed

- New private helper ``RuleEmitter.NeedsNullGuard(ITypeSymbol)`` returning ``false`` only for non-nullable value types. Class types and ``Nullable<T>`` keep the guard (the latter lowers to ``.HasValue`` — correct behaviour).
- Both ``EmitCollectionValidatorForProp`` and ``EmitNestedValidatorForProp`` consult the predicate before emitting the inner ``is not null`` line + closing brace.
- ``EmitCollectionValidatorForProp`` signature gains ``INamedTypeSymbol elementType`` — caller (``EmitCollectionValidators``) already builds the tuple ``(Property, ElementType)``, so no upstream change beyond the parameter wiring.
- 5 new generator-snapshot tests (``NestedValidatorValueTypeTests.cs``) cover the matrix:
  - collection-element × {struct, class, ``Nullable<struct>``}
  - scalar nested-property × {struct, class}

## Decisions ([design doc](docs/plans/2026-05-27-nested-validator-value-type-design.md))

- **Single helper, two call sites.** Same predicate, same fix; deduplicating closes the bug class.
- **``Nullable<T>`` keeps the guard.** ``is not null`` lowers to ``.HasValue``, which is the design-intended "skip validation when the underlying struct isn't present." Dropping the guard would force ``Validate(default(T))`` against an unset Nullable — wrong.
- **Indentation drift in ``.g.cs`` accepted.** ``csc`` ignores indentation; generator output isn't hand-read.

## SemVer

``1.4.0`` → ``1.4.1`` (patch — pure bug fix, no public API change, no new diagnostic).

## Test plan

- [x] ``dotnet test -c Release`` — all green locally on net8/net9/net10 (293 + 5 + 6 + 4 + 2 + 2 = ~936 test executions before this branch; +5 new generator tests in this PR)
- [ ] CI — green on this PR
- [ ] Follow-up after 1.4.1 propagates: ``ZeroAlloc.Templates`` follow-up branch ``feat/migrate-validate-types-to-record-struct`` migrates ``za-clean``'s ``OrderItem`` to ``readonly record struct`` for full struct parity with ``za-vertical-slice``.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

### Task 5.3: Watch CI + admin-merge + verify NuGet + mark B3 shipped

```bash
gh pr checks --watch
```

If anything fails, diagnose on-branch and push fixes. Likely failure modes: snapshot drift in another generator test if the new helper's emit ordering shifts; `AnalyzerReleases.Unshipped.md` doesn't need updating (no new diagnostic), but the release-tracking analyzer may run anyway — should be a no-op.

When green:

```bash
gh pr merge --admin --squash --delete-branch
```

Release-please opens ``chore(main): release 1.4.1``. Admin-merge that too. Verify NuGet (typically 2-5 min):

```bash
curl -s "https://api.nuget.org/v3-flatcontainer/zeroalloc.validation/index.json" \
  | python -c "import sys, json; v = json.load(sys.stdin)['versions']; print('latest:', v[-1])"
```

Expected: ``latest: 1.4.1``.

**B3 hygiene** — strike B3 in ``docs/backlog.md`` once 1.4.1 is on NuGet (same pattern B2's commit ``5154fbb`` follows: strikethrough heading + ``— ✅ shipped 1.4.1 (2026-05-27)`` + prepend a Shipped block; original body kept in ``<details>``). Commit on ``main`` with ``docs(backlog): mark B3 shipped (1.4.1)``.

---

## Verification checklist

- [ ] **Phase 1:** Collection-of-struct compiles cleanly; generated ``.g.cs`` lacks ``_c0Item is not null``.
- [ ] **Phase 2:** Class regression net + ``Nullable<struct>`` regression net both keep the guard.
- [ ] **Phase 3:** Scalar nested-property struct compiles; generated ``.g.cs`` lacks ``instance.Inner is not null``.
- [ ] **Phase 4:** Scalar class regression net keeps the guard.
- [ ] **Phase 5:** B3 filed; CI green; admin-merged; release-please cuts 1.4.1; NuGet propagates; backlog marked shipped.

## Out of scope (deferred, separate brainstorm)

- **Diagnostic surfacing.** No ``ZV00NN`` warning for the value-type code path. The case is "things compile now where they didn't before."
- **Generic type parameters as element types** (``IReadOnlyList<T>`` where T is an unconstrained generic parameter). Rare; no documented pattern hits it; separate brainstorm if it surfaces.
- **B1** (value-object-aware property validators) — independent. Separate session.
- **Template migration follow-through.** Bump ``ZeroAlloc.Templates``'s pinned ``ZeroAlloc.Validation`` from 1.4.0 → 1.4.1 and complete ``za-clean``'s ``OrderItem`` migration. Lives in the ``feat/migrate-validate-types-to-record-struct`` branch over there.
