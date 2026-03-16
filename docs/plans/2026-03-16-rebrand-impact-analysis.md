# Impact Analysis: ZValidation → ZeroAlloc.Validation Rebrand

## Summary

| Metric | Value |
|---|---|
| Date | 2026-03-16 |
| Refactor Type | Rename (namespace + package + directory) |
| Targets | 4 (namespace `ZValidation`, namespace `ZValidationInternal`, project prefix `ZValidation`, solution file) |
| Directly Affected Files | 113 |
| Transitively Affected Files | 24 (documentation) |
| Total Affected Files | 137 |
| Breaking Changes | 113 (all require updates to compile) |
| Risks Identified | 4 |
| Risk Level | Medium |

## Rename Map

| Old | New |
|---|---|
| `namespace ZValidation` | `namespace ZeroAlloc.Validation` |
| `namespace ZValidationInternal` | `namespace ZeroAlloc.Validation.Internal` |
| `using ZValidation` / `using ZValidation.*` | `using ZeroAlloc.Validation` / `using ZeroAlloc.Validation.*` |
| FQN string `"ZValidation.X"` | `"ZeroAlloc.Validation.X"` |
| FQN string `"ZValidationInternal.X"` | `"ZeroAlloc.Validation.Internal.X"` |
| `global::ZValidation.X` (emitted code) | `global::ZeroAlloc.Validation.X` |
| `global::ZValidationInternal.X` (emitted code) | `global::ZeroAlloc.Validation.Internal.X` |
| `src/ZValidation/` dir | `src/ZeroAlloc.Validation/` |
| `src/ZValidation.Generator/` dir | `src/ZeroAlloc.Validation.Generator/` |
| `src/ZValidation.Testing/` dir | `src/ZeroAlloc.Validation.Testing/` |
| `src/ZValidation.AspNetCore/` dir | `src/ZeroAlloc.Validation.AspNetCore/` |
| `src/ZValidation.AspNetCore.Generator/` dir | `src/ZeroAlloc.Validation.AspNetCore.Generator/` |
| `tests/ZValidation.Tests/` dir | `tests/ZeroAlloc.Validation.Tests/` |
| `tests/ZValidation.Tests.AspNetCore/` dir | `tests/ZeroAlloc.Validation.Tests.AspNetCore/` |
| `tests/ZValidation.Tests.MSTest/` dir | `tests/ZeroAlloc.Validation.Tests.MSTest/` |
| `tests/ZValidation.Tests.NUnit/` dir | `tests/ZeroAlloc.Validation.Tests.NUnit/` |
| `ZValidation.slnx` | `ZeroAlloc.Validation.slnx` |
| Diagnostic category `"ZValidation"` | `"ZeroAlloc.Validation"` |

**NOT renamed** (generated API surface — keep for now, can be addressed separately):
- `ZValidationActionFilter` (generated class in AspNetCore)
- `ZValidationServiceCollectionExtensions` (generated class in AspNetCore)
- `AddZValidationAutoValidation()` (generated extension method)

---

## Affected Files

### Group A — Source Namespaces (Breaking)

| # | File | Change |
|---|---|---|
| 1 | `src/ZValidation/Attributes/CustomValidationAttribute.cs` | `namespace ZValidation` → `namespace ZeroAlloc.Validation` |
| 2 | `src/ZValidation/Attributes/DisplayNameAttribute.cs` | same |
| 3 | `src/ZValidation/Attributes/EmailAddressAttribute.cs` | same |
| 4 | `src/ZValidation/Attributes/EmptyAttribute.cs` | same |
| 5 | `src/ZValidation/Attributes/EqualAttribute.cs` | same |
| 6 | `src/ZValidation/Attributes/ExclusiveBetweenAttribute.cs` | same |
| 7 | `src/ZValidation/Attributes/GreaterThanAttribute.cs` | same |
| 8 | `src/ZValidation/Attributes/GreaterThanOrEqualToAttribute.cs` | same |
| 9 | `src/ZValidation/Attributes/InclusiveBetweenAttribute.cs` | same |
| 10 | `src/ZValidation/Attributes/IsEnumNameAttribute.cs` | same |
| 11 | `src/ZValidation/Attributes/IsInEnumAttribute.cs` | same |
| 12 | `src/ZValidation/Attributes/LengthAttribute.cs` | same |
| 13 | `src/ZValidation/Attributes/LessThanAttribute.cs` | same |
| 14 | `src/ZValidation/Attributes/LessThanOrEqualToAttribute.cs` | same |
| 15 | `src/ZValidation/Attributes/MatchesAttribute.cs` | same |
| 16 | `src/ZValidation/Attributes/MaxLengthAttribute.cs` | same |
| 17 | `src/ZValidation/Attributes/MinLengthAttribute.cs` | same |
| 18 | `src/ZValidation/Attributes/MustAttribute.cs` | same |
| 19 | `src/ZValidation/Attributes/NotEmptyAttribute.cs` | same |
| 20 | `src/ZValidation/Attributes/NotEqualAttribute.cs` | same |
| 21 | `src/ZValidation/Attributes/NotNullAttribute.cs` | same |
| 22 | `src/ZValidation/Attributes/NullAttribute.cs` | same |
| 23 | `src/ZValidation/Attributes/PrecisionScaleAttribute.cs` | same |
| 24 | `src/ZValidation/Attributes/SkipWhenAttribute.cs` | same |
| 25 | `src/ZValidation/Attributes/StopOnFirstFailureAttribute.cs` | same |
| 26 | `src/ZValidation/Attributes/ValidateAttribute.cs` | same |
| 27 | `src/ZValidation/Attributes/ValidateWithAttribute.cs` | same |
| 28 | `src/ZValidation/Attributes/ValidationAttribute.cs` | same |
| 29 | `src/ZValidation/Core/Severity.cs` | `namespace ZValidation` → `namespace ZeroAlloc.Validation` |
| 30 | `src/ZValidation/Core/ValidationContext.cs` | same |
| 31 | `src/ZValidation/Core/ValidationFailure.cs` | same |
| 32 | `src/ZValidation/Core/ValidationResult.cs` | same |
| 33 | `src/ZValidation/Core/ValidatorFor.cs` | same |
| 34 | `src/ZValidation/Internal/DecimalValidator.cs` | `namespace ZValidationInternal` → `namespace ZeroAlloc.Validation.Internal` |
| 35 | `src/ZValidation/Internal/EmailValidator.cs` | same |
| 36 | `src/ZValidation/Internal/FailureBuffer.cs` | same |
| 37 | `src/ZValidation.Generator/RuleEmitter.cs` | `namespace ZValidation.Generator` → `namespace ZeroAlloc.Validation.Generator` + all FQN strings (28 constants + 16 code-gen strings) |
| 38 | `src/ZValidation.Generator/ValidatorGenerator.cs` | `namespace ZValidation.Generator` → `namespace ZeroAlloc.Validation.Generator` + 7 FQN/string changes + 3 diagnostic category strings |
| 39 | `src/ZValidation.Testing/ValidationAssert.cs` | `namespace ZValidation.Testing` → `namespace ZeroAlloc.Validation.Testing` |
| 40 | `src/ZValidation.Testing/ValidationAssertException.cs` | same |
| 41 | `src/ZValidation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs` | `namespace ZValidation.AspNetCore.Generator` → `namespace ZeroAlloc.Validation.AspNetCore.Generator` + FQN string line 11 + emitted type ref line 83 |

### Group B — Project Files (Breaking)

| # | File | Change |
|---|---|---|
| 42 | `src/ZValidation/ZValidation.csproj` | rename to `ZeroAlloc.Validation.csproj`; update ProjectReference path for Generator |
| 43 | `src/ZValidation.Generator/ZValidation.Generator.csproj` | rename to `ZeroAlloc.Validation.Generator.csproj` |
| 44 | `src/ZValidation.Testing/ZValidation.Testing.csproj` | rename to `ZeroAlloc.Validation.Testing.csproj`; update ProjectReference path |
| 45 | `src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj` | rename to `ZeroAlloc.Validation.AspNetCore.csproj`; update 2 ProjectReference paths |
| 46 | `src/ZValidation.AspNetCore.Generator/ZValidation.AspNetCore.Generator.csproj` | rename to `ZeroAlloc.Validation.AspNetCore.Generator.csproj` |
| 47 | `tests/ZValidation.Tests/ZValidation.Tests.csproj` | rename to `ZeroAlloc.Validation.Tests.csproj`; update 4 ProjectReference paths |
| 48 | `tests/ZValidation.Tests.AspNetCore/ZValidation.Tests.AspNetCore.csproj` | rename to `ZeroAlloc.Validation.Tests.AspNetCore.csproj`; update 4 ProjectReference paths |
| 49 | `tests/ZValidation.Tests.MSTest/ZValidation.Tests.MSTest.csproj` | rename to `ZeroAlloc.Validation.Tests.MSTest.csproj`; update 2 ProjectReference paths |
| 50 | `tests/ZValidation.Tests.NUnit/ZValidation.Tests.NUnit.csproj` | rename to `ZeroAlloc.Validation.Tests.NUnit.csproj`; update 2 ProjectReference paths |

### Group C — Solution File (Breaking)

| # | File | Change |
|---|---|---|
| 51 | `ZValidation.slnx` | rename to `ZeroAlloc.Validation.slnx`; update all 9 project paths |

### Group D — Analyzer Config (Update Required)

| # | File | Change |
|---|---|---|
| 52 | `src/ZValidation.Generator/AnalyzerReleases.Unshipped.md` | Category `ZValidation` → `ZeroAlloc.Validation` on 3 lines |

### Group E — Test Source Files (Breaking — using statements + namespace)

All 66 test .cs files across 4 test projects. The changes per file are:
- `namespace ZValidation.Tests.*` → `namespace ZeroAlloc.Validation.Tests.*`
- `using ZValidation;` → `using ZeroAlloc.Validation;`
- `using ZValidation.Testing;` → `using ZeroAlloc.Validation.Testing;`
- `using ZValidation.Generator;` → `using ZeroAlloc.Validation.Generator;` (2 files)

| # | File |
|---|---|
| 53–118 | All .cs files in `tests/ZValidation.Tests/` (55 files) |
| 119–122 | All .cs files in `tests/ZValidation.Tests.AspNetCore/` (4 files) |
| 123–125 | All .cs files in `tests/ZValidation.Tests.MSTest/` (~3 files) |
| 126–128 | All .cs files in `tests/ZValidation.Tests.NUnit/` (~3 files) |

### Group F — Documentation (Update Required)

| # | File |
|---|---|
| 129 | `docs/features.md` |
| 130–137+ | All 24 `docs/plans/*.md` files containing "ZValidation" |

---

## Risk Register

| # | Risk | Affected Files | Severity | Mitigation |
|---|---|---|---|---|
| 1 | **Generator FQN mismatch** — if `RuleEmitter.cs` FQN strings are not updated, generated validators emit `global::ZValidation.X` which no longer exists, causing compile errors in all consuming projects | `RuleEmitter.cs`, `ValidatorGenerator.cs`, `AspNetCoreFilterEmitter.cs` | High | Update all FQN constants and code-gen strings atomically with the namespace rename; run full test suite as verification |
| 2 | **Internal namespace emitted code** — `FailureBuffer`, `EmailValidator`, `DecimalValidator` are referenced as `global::ZValidationInternal.X` in generated code; the new namespace `ZeroAlloc.Validation.Internal` changes the FQN | `RuleEmitter.cs` lines 116, 625, 637; `FailureBuffer.cs`, `EmailValidator.cs`, `DecimalValidator.cs` | High | Update both the namespace declarations in `.cs` files AND the emitted string literals in `RuleEmitter.cs` together |
| 3 | **`using ZValidationInternal` test helper** — generator emission tests that compile source snippets inline `using ZValidation;` in test sources; these helper strings must also be updated | `GeneratorRuleEmissionTests.cs`, `GeneratorDiagnosticTests.cs` | Medium | Search for inline `using ZValidation` strings inside test sources embedded in string literals; update alongside other test files |
| 4 | **Doc files** — 24 markdown docs contain code examples with `namespace ZValidation;`, `using ZValidation;`, project names, etc. | All files in `docs/plans/`, `docs/features.md` | Low | Bulk find-replace is safe since docs are not compiled; verify manually on `features.md` |

---

## Execution Order

### Group 1: Directory and file renames (atomic, do together)
Use `git mv` for all renames so git tracks history correctly.

```bash
# Source directories
git mv src/ZValidation src/ZeroAlloc.Validation
git mv src/ZValidation.Generator src/ZeroAlloc.Validation.Generator
git mv src/ZValidation.Testing src/ZeroAlloc.Validation.Testing
git mv src/ZValidation.AspNetCore src/ZeroAlloc.Validation.AspNetCore
git mv src/ZValidation.AspNetCore.Generator src/ZeroAlloc.Validation.AspNetCore.Generator

# Test directories
git mv tests/ZValidation.Tests tests/ZeroAlloc.Validation.Tests
git mv tests/ZValidation.Tests.AspNetCore tests/ZeroAlloc.Validation.Tests.AspNetCore
git mv tests/ZValidation.Tests.MSTest tests/ZeroAlloc.Validation.Tests.MSTest
git mv tests/ZValidation.Tests.NUnit tests/ZeroAlloc.Validation.Tests.NUnit

# .csproj files (now inside new dirs)
git mv src/ZeroAlloc.Validation/ZValidation.csproj src/ZeroAlloc.Validation/ZeroAlloc.Validation.csproj
git mv src/ZeroAlloc.Validation.Generator/ZValidation.Generator.csproj src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj
git mv src/ZeroAlloc.Validation.Testing/ZValidation.Testing.csproj src/ZeroAlloc.Validation.Testing/ZeroAlloc.Validation.Testing.csproj
git mv src/ZeroAlloc.Validation.AspNetCore/ZValidation.AspNetCore.csproj src/ZeroAlloc.Validation.AspNetCore/ZeroAlloc.Validation.AspNetCore.csproj
git mv "src/ZeroAlloc.Validation.AspNetCore.Generator/ZValidation.AspNetCore.Generator.csproj" "src/ZeroAlloc.Validation.AspNetCore.Generator/ZeroAlloc.Validation.AspNetCore.Generator.csproj"
git mv tests/ZeroAlloc.Validation.Tests/ZValidation.Tests.csproj tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
git mv "tests/ZeroAlloc.Validation.Tests.AspNetCore/ZValidation.Tests.AspNetCore.csproj" "tests/ZeroAlloc.Validation.Tests.AspNetCore/ZeroAlloc.Validation.Tests.AspNetCore.csproj"
git mv "tests/ZeroAlloc.Validation.Tests.MSTest/ZValidation.Tests.MSTest.csproj" "tests/ZeroAlloc.Validation.Tests.MSTest/ZeroAlloc.Validation.Tests.MSTest.csproj"
git mv "tests/ZeroAlloc.Validation.Tests.NUnit/ZValidation.Tests.NUnit.csproj" "tests/ZeroAlloc.Validation.Tests.NUnit/ZeroAlloc.Validation.Tests.NUnit.csproj"

# Solution file
git mv ZValidation.slnx ZeroAlloc.Validation.slnx
```

**Checkpoint:** `git status` to confirm all renames tracked. Build will be broken here.

### Group 2: Solution file content
Update `ZeroAlloc.Validation.slnx` — all 9 `<Project Path="...">` values to new paths.

**Checkpoint:** No build yet (project files still have old internal paths).

### Group 3: .csproj file content (ProjectReference paths)
Update internal content of all 9 `.csproj` files — `ProjectReference` paths point to new directories/filenames.

**Checkpoint:** `dotnet build` should succeed here (namespaces are still old but compilation works).

### Group 4: Source namespaces + using statements (bulk find-replace)
All 41 source `.cs` files:
- `namespace ZValidation` → `namespace ZeroAlloc.Validation`
- `namespace ZValidationInternal` → `namespace ZeroAlloc.Validation.Internal`

**Checkpoint:** `dotnet build` after this group. Test files still broken (using stale names) but source compiles.

### Group 5: Generator FQN strings (critical — atomic with Group 4)
Update `RuleEmitter.cs`, `ValidatorGenerator.cs`, `AspNetCoreFilterEmitter.cs`:
- All 28 FQN constants (`"ZValidation.X"` → `"ZeroAlloc.Validation.X"`)
- All 16+ code-gen emitted strings (`global::ZValidation.X` → `global::ZeroAlloc.Validation.X`)
- 3 internal FQN strings (`ZValidationInternal.X` → `ZeroAlloc.Validation.Internal.X`)
- 3 diagnostic category strings (`"ZValidation"` → `"ZeroAlloc.Validation"`)
- `AnalyzerReleases.Unshipped.md` categories

**Checkpoint:** `dotnet build src/` — source + generator fully compiles.

### Group 6: Test files (using statements + namespaces)
All 66 test `.cs` files across 4 test projects. Bulk find-replace:
- `using ZValidation;` → `using ZeroAlloc.Validation;`
- `using ZValidation.Testing;` → `using ZeroAlloc.Validation.Testing;`
- `using ZValidation.Generator;` → `using ZeroAlloc.Validation.Generator;`
- `namespace ZValidation.Tests` → `namespace ZeroAlloc.Validation.Tests`

**Also check:** Inline source strings in generator emission tests (e.g., `"using ZValidation;"` embedded in test string literals) — these must also be updated.

**Checkpoint:** `dotnet test` — all 243 tests must pass.

### Group 7: Documentation
Bulk find-replace in all 24+ `.md` files:
- `ZValidation` → `ZeroAlloc.Validation`
- `ZValidationInternal` → `ZeroAlloc.Validation.Internal`

**Checkpoint:** Manual review of `docs/features.md` for correctness.

---

## Commit Strategy

| Commit | Content |
|---|---|
| `refactor: rename directories and files for ZeroAlloc.Validation rebrand` | Group 1 (git mv only) |
| `refactor: update solution and project file references` | Groups 2 + 3 |
| `refactor: rename namespaces and update generator FQNs to ZeroAlloc.Validation` | Groups 4 + 5 (atomic — must be together) |
| `refactor: update test namespaces and using statements` | Group 6 |
| `docs: update all documentation for ZeroAlloc.Validation rebrand` | Group 7 |
