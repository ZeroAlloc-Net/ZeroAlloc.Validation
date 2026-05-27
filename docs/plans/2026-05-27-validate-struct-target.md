# `[Validate]` struct target — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship ZeroAlloc.Validation 1.3.0 widening `[Validate]` from `Class` to `Class | Struct`, with `ZV0014` (Warning) firing on non-readonly structs.

**Architecture:** Two-line attribute change + four-token generator predicate widening + one new `DiagnosticDescriptor`. The existing rule-emission paths are `TypeKind`-agnostic and require no edits. Closes backlog B2.

**Tech Stack:** .NET 10, Roslyn incremental generator (`IIncrementalGenerator`), xUnit, `AnalyzerReleases.Unshipped.md`.

**Design doc:** `docs/plans/2026-05-27-validate-struct-target-design.md` (committed at `13f5c0b`).

**Working branch:** `feat/validate-struct-target` (already created off `main`; design committed).

---

## Phase 0 — Orientation (5 min)

Before touching code, skim three files so the rest of the plan reads in context. No commits in this phase.

### Task 0.1: Read the existing attribute + generator entry

**Files (read-only):**
- `src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs` — current `AttributeTargets.Class`. The single line to change.
- `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs:58-78` — `Initialize` registers the `ForAttributeWithMetadataName` pipeline. Line 66's predicate is the syntax-node filter.
- `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs:81` — the `Emit` method runs per matched symbol. `ZV0014` reporting hooks in here.
- `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs:26-56` — existing `DiagnosticDescriptor` declarations (`ZV0011 / ZV0012 / ZV0013 / ZV0015`). `ZV0014` follows the same shape and slots in to fill the gap.

### Task 0.2: Read one existing integration test + one existing generator test

**Files (read-only):**
- `tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs` — see how integration tests instantiate `XValidator` directly and assert on `ValidationResult`. The new struct tests follow this exact shape.
- `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorDiscoveryTests.cs` — see how generator tests construct a `CSharpCompilation`, run the generator, and inspect `Diagnostics` / `GeneratedTrees`. The `ZV0014` tests follow this shape.

---

## Phase 1 — Widen attribute target via failing test (TDD) (30 min, 5 tasks)

### Task 1.1: Write the failing integration test for `readonly record struct`

**Files:**
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/StructValidationTests.cs`

**Step 1: Write the test file**

```csharp
using Xunit;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public class StructValidationTests
{
    [Fact]
    public void ReadonlyRecordStruct_Validate_HappyPath_ReportsValid()
    {
        var validator = new RrsCommandValidator();
        var result = validator.Validate(new RrsCommand(Total: 10));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ReadonlyRecordStruct_Validate_SadPath_ReportsFailureOnTotal()
    {
        var validator = new RrsCommandValidator();
        var result = validator.Validate(new RrsCommand(Total: 0));
        Assert.False(result.IsValid);
        Assert.Equal(nameof(RrsCommand.Total), result.Failures[0].PropertyName);
    }
}

[Validate]
public readonly record struct RrsCommand([property: GreaterThan(0)] int Total);
```

**Step 2: Run the test — expect BUILD FAIL**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet build tests/ZeroAlloc.Validation.Tests -c Release
```

Expected: `CS0592: Attribute 'Validate' is not valid on this declaration type. It is only valid on 'class' declarations.`

This is the gate — the test must fail-build for the same reason a real consumer fails today. If it builds, the attribute target was already widened or the test isn't applying it to a struct.

**No commit yet** — test is failing.

### Task 1.2: Widen `AttributeTargets` on `ValidateAttribute`

**Files:**
- Modify: `src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs`

**Step 1: Change the `[AttributeUsage]` flags**

```csharp
namespace ZeroAlloc.Validation;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class ValidateAttribute : Attribute
{
    public bool StopOnFirstFailure { get; set; }
}
```

**Step 2: Build the attribute project**

```bash
dotnet build src/ZeroAlloc.Validation -c Release
```

Expected: build succeeds, no warnings.

**Step 3: Build the test project — expect BUILD STILL FAILS, but with a different error**

```bash
dotnet build tests/ZeroAlloc.Validation.Tests -c Release
```

Expected: `CS0246: The type or namespace name 'RrsCommandValidator' could not be found`. The attribute now accepts the struct target, but the generator hasn't emitted the validator class. This is the next gate.

**No commit yet** — generator widening is the same logical unit of work.

### Task 1.3: Widen the generator's syntax-node predicate

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` (around line 66)

**Step 1: Replace the predicate**

Find:

```csharp
predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
```

Replace with:

```csharp
// All four C# shapes — class, record (class), struct, record struct — hit
// the same emission path; the downstream generator walks symbol properties
// identically regardless of TypeKind.
predicate: static (node, _) =>
    node is ClassDeclarationSyntax
         or RecordDeclarationSyntax
         or StructDeclarationSyntax
         or RecordStructDeclarationSyntax,
```

`StructDeclarationSyntax` and `RecordStructDeclarationSyntax` are already reachable via the existing `using Microsoft.CodeAnalysis.CSharp.Syntax;` at the top of the file — no new `using` needed.

**Step 2: Build the generator**

```bash
dotnet build src/ZeroAlloc.Validation.Generator -c Release
```

Expected: build succeeds.

### Task 1.4: Run the failing test — now expect PASS

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet test tests/ZeroAlloc.Validation.Tests -c Release --filter "FullyQualifiedName~StructValidationTests"
```

Expected: 2/2 pass (`ReadonlyRecordStruct_Validate_HappyPath_ReportsValid`, `ReadonlyRecordStruct_Validate_SadPath_ReportsFailureOnTotal`).

If `CS0246` still fires, the generator's incremental cache is stale — run `dotnet build --no-incremental tests/ZeroAlloc.Validation.Tests` to force a fresh pipeline.

### Task 1.5: Run the full test suite — verify no regressions

```bash
dotnet test -c Release
```

Expected: every existing test passes. The `class` / `record` paths are unchanged; the new predicate is purely additive.

### Task 1.6: Commit

```bash
git add src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs \
        src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs \
        tests/ZeroAlloc.Validation.Tests/Integration/StructValidationTests.cs
git commit -m "feat(generator): [Validate] now accepts readonly record struct

Widens AttributeTargets.Class to Class | Struct on ValidateAttribute
and adds StructDeclarationSyntax / RecordStructDeclarationSyntax to the
generator's syntax-node predicate. The downstream rule-emission paths
already walked symbol properties TypeKind-agnostically, so accepting
struct targets needed no further changes.

Tests cover readonly record struct happy + sad paths; readonly struct
(without record) and the non-readonly diagnostic land in follow-up
commits in the same release cycle."
```

---

## Phase 2 — Cover `readonly struct` shape (15 min, 2 tasks)

### Task 2.1: Add `readonly struct` happy + sad path tests

**Files:**
- Modify: `tests/ZeroAlloc.Validation.Tests/Integration/StructValidationTests.cs`

**Step 1: Append two `[Fact]`s + the matching model**

After the existing `ReadonlyRecordStruct_*` tests, add:

```csharp
    [Fact]
    public void ReadonlyStruct_Validate_HappyPath_ReportsValid()
    {
        var validator = new RsCommandValidator();
        var result = validator.Validate(new RsCommand(10));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ReadonlyStruct_Validate_SadPath_ReportsFailureOnTotal()
    {
        var validator = new RsCommandValidator();
        var result = validator.Validate(new RsCommand(0));
        Assert.False(result.IsValid);
        Assert.Equal(nameof(RsCommand.Total), result.Failures[0].PropertyName);
    }
}

[Validate]
public readonly struct RsCommand
{
    [GreaterThan(0)]
    public int Total { get; }

    public RsCommand(int total) => Total = total;
}
```

(Move the closing `}` of the class above the new `[Validate]` declaration.)

### Task 2.2: Run + commit

```bash
dotnet test -c Release --filter "FullyQualifiedName~StructValidationTests"
```

Expected: 4/4 pass.

```bash
git add tests/ZeroAlloc.Validation.Tests/Integration/StructValidationTests.cs
git commit -m "test(generator): readonly struct happy/sad-path coverage"
```

---

## Phase 3 — `ZV0014` diagnostic for non-readonly structs (30 min, 5 tasks)

### Task 3.1: Write the failing diagnostic test

**Files:**
- Create: `tests/ZeroAlloc.Validation.Tests/Generator/StructValidationDiagnosticTests.cs`

**Step 1: Write the test file**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class StructValidationDiagnosticTests
{
    [Fact]
    public void NonReadonly_RecordStruct_With_Validate_Fires_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public record struct MutableRs([property: GreaterThan(0)] int Total);
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "ZV0014");
    }

    [Fact]
    public void NonReadonly_Struct_With_Validate_Fires_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public struct MutableS
            {
                [GreaterThan(0)]
                public int Total { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "ZV0014");
    }

    [Fact]
    public void Readonly_RecordStruct_With_Validate_Does_Not_Fire_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public readonly record struct CleanRrs([property: GreaterThan(0)] int Total);
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "ZV0014");
    }

    [Fact]
    public void Readonly_Struct_With_Validate_Does_Not_Fire_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public readonly struct CleanRs
            {
                [GreaterThan(0)]
                public int Total { get; }
                public CleanRs(int total) => Total = total;
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "ZV0014");
    }

    [Fact]
    public void Class_With_Validate_Does_Not_Fire_ZV0014()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public class CleanClass
            {
                [GreaterThan(0)]
                public int Total { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "ZV0014");
    }

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

**Step 2: Run — expect 2 FAIL (the ZV0014-expecting tests) + 3 PASS (the regression nets)**

```bash
dotnet test -c Release --filter "FullyQualifiedName~StructValidationDiagnosticTests"
```

Expected: `Failed: 2, Passed: 3`. The two `Fires_ZV0014` tests fail because the diagnostic isn't emitted yet; the three "Does_Not_Fire" tests pass trivially (no diagnostics are emitted from any shape yet).

**No commit yet** — diagnostic implementation is the next step.

### Task 3.2: Declare the `ZV0014` `DiagnosticDescriptor`

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` (after the `ZV0013` declaration, before `ZV0015`)

**Step 1: Add the descriptor**

Insert between `ZV0013` (lines 42-48) and `ZV0015` (lines 50-56):

```csharp
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

### Task 3.3: Report `ZV0014` inside `Emit`

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` — the `Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol, BehaviorCache allBehaviors)` method around line 81.

**Step 1: Add the diagnostic report at the top of `Emit`**

Find the first line of the `Emit` method body and insert the check before any emission work:

```csharp
    private static void Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol, BehaviorCache allBehaviors)
    {
        // ZV0014 — surface mutability hazard on non-readonly structs. Generator
        // still proceeds to emit the validator; the warning is informational.
        if (classSymbol.TypeKind == TypeKind.Struct && !classSymbol.IsReadOnly)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                ZV0014,
                classSymbol.Locations.FirstOrDefault() ?? Location.None,
                classSymbol.Name));
        }

        // …existing Emit body continues here unchanged…
```

`System.Linq` is already imported (line 2 of the file) so `Locations.FirstOrDefault()` resolves.

### Task 3.4: Re-run the diagnostic tests — expect 5/5 PASS

```bash
dotnet test -c Release --filter "FullyQualifiedName~StructValidationDiagnosticTests"
```

Expected: all 5 pass — both `Fires_ZV0014` tests now find the diagnostic, the three regression nets stay clean.

### Task 3.5: Update `AnalyzerReleases.Unshipped.md`

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md`

**Step 1: Append the new-rule row**

```markdown
### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|------------------------------
ZV0014  | ZeroAlloc.Validation | Warning  | [Validate] on non-readonly struct
```

If the file already has a `### New Rules` header (it's normal during a release cycle), append the `ZV0014` line under it instead of creating a duplicate header.

### Task 3.6: Run the full suite + commit

```bash
dotnet test -c Release
```

Expected: every test passes — 9 new + every existing.

```bash
git add src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs \
        src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md \
        tests/ZeroAlloc.Validation.Tests/Generator/StructValidationDiagnosticTests.cs
git commit -m "feat(generator): ZV0014 warning on non-readonly struct with [Validate]

Non-readonly structs allow callers to mutate the instance between the
validator returning success and the consumer reading the value, leaving
the validation result stale. ZV0014 is a Warning (build-visible, not
fatal); pragma-disable on a case-by-case basis when the consumer
cooperates with the hazard. Generator still emits the validator —
the warning is informational, not a refusal."
```

---

## Phase 4 — Docs (15 min, 2 tasks)

### Task 4.1: Update `docs/getting-started.md`

**Files:**
- Modify: `docs/getting-started.md`

**Step 1:** Find the section where `[Validate]` is first introduced (typically titled "Decorate your request type" or similar — search for the literal `[Validate]` in the doc).

**Step 2:** Add this callout immediately after the first `[Validate]` mention:

```markdown
> **Target types.** `[Validate]` works on `class`, `record`, `readonly struct`, and
> `readonly record struct`. Decorating a non-readonly `struct` or `record struct`
> emits `ZV0014` (Warning) — a caller can mutate the instance between the
> validator returning success and the consumer reading the value, making the
> validation result stale. Prefer the `readonly` form for request types.
```

### Task 4.2: Add `ZV0014` to `docs/error-messages.md` + commit

**Files:**
- Modify: `docs/error-messages.md`

**Step 1:** Find the existing `## ZV0013` / `## ZV0015` headers (or whatever format `error-messages.md` uses for per-diagnostic entries). Insert between them:

```markdown
## ZV0014 — `[Validate]` on non-readonly struct

**Severity:** Warning

**What it means.** You decorated a `struct` or `record struct` with `[Validate]`,
but the type is not declared `readonly`. A caller can mutate the instance
between the validator returning `IsValid == true` and the consumer reading
the value — making the validation result stale.

**Fix.** Declare the type as `readonly struct` or `readonly record struct`:

```csharp
[Validate]
public readonly record struct PlaceOrderCommand(
    [property: GreaterThan(0)] int CustomerId,
    [property: GreaterThan(0)] decimal Total);
```

**Suppressing.** If your call site cooperates with the hazard (e.g. you validate
inside the same method that constructs the struct and never mutate after),
suppress with `#pragma warning disable ZV0014` around the type declaration,
or add `<NoWarn>$(NoWarn);ZV0014</NoWarn>` in the consuming project.
```

**Step 2:** Commit.

```bash
git add docs/getting-started.md docs/error-messages.md
git commit -m "docs: ZV0014 + struct target callout in getting-started"
```

---

## Phase 5 — Verify + ship (15 min, 4 tasks)

### Task 5.1: Run the full repo test suite one final time

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet test -c Release
```

Expected: every test passes — the 4 new integration tests, the 5 new diagnostic tests, and every existing test from before this branch.

If anything is red, fix on-branch before pushing.

### Task 5.2: Push the branch + open the PR

```bash
git push -u origin feat/validate-struct-target
gh pr create \
  --title "feat(generator): [Validate] now accepts struct and record struct" \
  --body "$(cat <<'EOF'
## Summary

Closes backlog B2. Widens `[Validate]` from `Class` to `Class | Struct` so the four C# shapes — `class`, `record`, `struct`, `record struct` — all participate. Non-readonly structs raise `ZV0014` (Warning).

Surfaced 2026-05-26 building the `za-vertical-slice` template ([ZeroAlloc.Templates#117](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/117)) — request types had to widen from `readonly record struct` to `sealed record` solely to attach `[Validate]`.

## What changed

- `ValidateAttribute` — target widened to `Class | Struct`.
- Generator syntax predicate — accepts `StructDeclarationSyntax` and `RecordStructDeclarationSyntax`.
- New `ZV0014` Warning when `[Validate]` decorates a non-readonly struct.
- 4 integration tests covering `readonly struct` and `readonly record struct` happy/sad paths.
- 5 generator diagnostic tests covering the matrix of shape × readonly-ness (class is the regression net).
- `docs/getting-started.md` callout + `docs/error-messages.md` ZV0014 entry.

## Decisions ([design doc](docs/plans/2026-05-27-validate-struct-target-design.md))

- **No `Validate(in T)` overload on `ValidatorFor<T>`.** Pass-by-value matches the existing base. For typical request structs (≤32 bytes) the stack copy is below noise on the measured allocation profile (Validator_Generated at 2.18 ns / 0 B). Deferred until a consumer measures the copy cost as load-bearing.
- **`ZV0014` is a Warning, not Error.** Hard refusal would be paternalistic for a v1.x minor — legitimate mutable-then-frozen patterns exist. Opt-out via single-line pragma is trivial.
- **No restriction to `readonly`.** Generator accepts all four shapes; the diagnostic raises the loud signal at compile time without blocking the build.

## SemVer

`1.2.0` → `1.3.0` (additive minor — existing class/record consumers see zero behaviour change).

## Test plan

- [x] `dotnet test -c Release` — all green locally
- [ ] CI — green on this PR
- [ ] After 1.3.0 propagates: ZeroAlloc.Templates follow-up PR migrates `za-vertical-slice`'s 4 request types from `sealed record` back to `readonly record struct`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

### Task 5.3: Watch CI

```bash
gh pr checks --watch
```

If anything fails, diagnose on-branch and push fixes. The likely failure modes are: snapshot drift in some other generator test that wasn't aware of the new predicate types, or `AnalyzerReleases.Unshipped.md` formatting tripping the release-tracking analyzer.

### Task 5.4: Admin-merge + release

After CI lands green:

```bash
gh pr merge --admin --squash --delete-branch
```

Release-please opens a release PR (`chore(main): release 1.3.0`). Admin-merge that too. NuGet propagation typically takes 2-5 minutes.

Verify:

```bash
curl -s "https://api.nuget.org/v3-flatcontainer/zeroalloc.validation/index.json" | python -c "import sys, json; v = json.load(sys.stdin)['versions']; print('latest:', v[-1])"
```

Expected: `latest: 1.3.0`.

### Task 5.5: Workspace + repo backlog hygiene

**Repo backlog** — edit `docs/backlog.md` to strike B2 and add the shipped marker:

```markdown
## ~~B2 — `[Validate]` on `record`, `record struct`, and `struct` targets~~ — ✅ shipped 1.3.0 (2026-05-27)

**Shipped:** as designed — `AttributeTargets.Class | AttributeTargets.Struct`,
generator predicate widened, ZV0014 Warning on non-readonly structs.
See `docs/plans/2026-05-27-validate-struct-target-design.md` + PR #XX.
```

(Strikethrough the heading and prepend the shipped block above the original body, matching how B1 will eventually be marked.)

Commit:

```bash
git checkout main && git pull
git add docs/backlog.md
git commit -m "docs(backlog): mark B2 shipped (1.3.0)"
git push
```

**Workspace BACKLOG.md** — `c:/Projects/Prive/ZeroAlloc/docs/BACKLOG.md`. Not git-tracked; this is a workspace edit only. Not strictly required, but if you maintain it, add a one-line entry under the ZeroAlloc.Validation section noting 1.3.0 shipped with B2.

---

## Verification checklist

- [ ] **Phase 1:** `[Validate]` decorates a `readonly record struct`; generator emits the validator class; 2 happy/sad tests pass.
- [ ] **Phase 2:** `readonly struct` works identically; 2 more tests pass.
- [ ] **Phase 3:** `ZV0014` fires on non-readonly `struct` + `record struct`; doesn't fire on `readonly` forms or on `class`; 5 diagnostic tests pass.
- [ ] **Phase 4:** `docs/getting-started.md` callout + `docs/error-messages.md` ZV0014 section landed.
- [ ] **Phase 5:** CI green, admin-merged, release-please cuts 1.3.0, NuGet propagates, B2 marked shipped in both backlogs.

## Out of scope (deferred, separate brainstorm)

- **B1 — value-object-aware property validators** (`[GreaterThan(0)] CustomerId Id` unwraps to `.Value`). Independent of this PR; ships as a separate 1.x minor after B2.
- **`Validate(in T)` overload on `ValidatorFor<T>`** for zero-copy struct validation. Deferred until benchmarks show the copy cost matters.
- **Template migration** — `za-vertical-slice` request types stay on `sealed record` until 1.3.0 propagates to NuGet, then a follow-up PR over there migrates them to `readonly record struct`.
