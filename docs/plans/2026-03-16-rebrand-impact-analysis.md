# Impact Analysis: ZeroAlloc.Validation → ZeroAlloc.Validation Rebrand

## Summary

| Metric | Value |
|---|---|
| Date | 2026-03-16 |
| Refactor Type | Rename (namespace + package + directory) |
| Targets | 4 (namespace `ZeroAlloc.Validation`, namespace `ZeroAlloc.Validation.Internal`, project prefix `ZeroAlloc.Validation`, solution file) |
| Directly Affected Files | 113 |
| Transitively Affected Files | 24 (documentation) |
| Total Affected Files | 137 |
| Breaking Changes | 113 (all require updates to compile) |
| Risks Identified | 4 |
| Risk Level | Medium |

## Rename Map

| Old | New |
|---|---|
| `namespace ZeroAlloc.Validation` | `namespace ZeroAlloc.Validation` |
| `namespace ZeroAlloc.Validation.Internal` | `namespace ZeroAlloc.Validation.Internal` |
| `using ZeroAlloc.Validation` / `using ZeroAlloc.Validation.*` | `using ZeroAlloc.Validation` / `using ZeroAlloc.Validation.*` |
| FQN string `"ZeroAlloc.Validation.X"` | `"ZeroAlloc.Validation.X"` |
| FQN string `"ZeroAlloc.Validation.Internal.X"` | `"ZeroAlloc.Validation.Internal.X"` |
| `global::ZeroAlloc.Validation.X` (emitted code) | `global::ZeroAlloc.Validation.X` |
| `global::ZeroAlloc.Validation.Internal.X` (emitted code) | `global::ZeroAlloc.Validation.Internal.X` |
| `src/ZeroAlloc.Validation/` dir | `src/ZeroAlloc.Validation/` |
| `src/ZeroAlloc.Validation.Generator/` dir | `src/ZeroAlloc.Validation.Generator/` |
| `src/ZeroAlloc.Validation.Testing/` dir | `src/ZeroAlloc.Validation.Testing/` |
| `src/ZeroAlloc.Validation.AspNetCore/` dir | `src/ZeroAlloc.Validation.AspNetCore/` |
| `src/ZeroAlloc.Validation.AspNetCore.Generator/` dir | `src/ZeroAlloc.Validation.AspNetCore.Generator/` |
| `tests/ZeroAlloc.Validation.Tests/` dir | `tests/ZeroAlloc.Validation.Tests/` |
| `tests/ZeroAlloc.Validation.Tests.AspNetCore/` dir | `tests/ZeroAlloc.Validation.Tests.AspNetCore/` |
| `tests/ZeroAlloc.Validation.Tests.MSTest/` dir | `tests/ZeroAlloc.Validation.Tests.MSTest/` |
| `tests/ZeroAlloc.Validation.Tests.NUnit/` dir | `tests/ZeroAlloc.Validation.Tests.NUnit/` |
| `ZeroAlloc.Validation.slnx` | `ZeroAlloc.Validation.slnx` |
| Diagnostic category `"ZeroAlloc.Validation"` | `"ZeroAlloc.Validation"` |

**NOT renamed** (generated API surface — keep for now, can be addressed separately):
- `ZValidationActionFilter` (generated class in AspNetCore)
- `ZValidationServiceCollectionExtensions` (generated class in AspNetCore)
- `AddZValidationAutoValidation()` (generated extension method)

---

## Affected Files

### Group A — Source Namespaces (Breaking)

| # | File | Change |
|---|---|---|
| 1 | `src/ZeroAlloc.Validation/Attributes/CustomValidationAttribute.cs` | `namespace ZeroAlloc.Validation` → `namespace ZeroAlloc.Validation` |
| 2 | `src/ZeroAlloc.Validation/Attributes/DisplayNameAttribute.cs` | same |
| 3 | `src/ZeroAlloc.Validation/Attributes/EmailAddressAttribute.cs` | same |
| 4 | `src/ZeroAlloc.Validation/Attributes/EmptyAttribute.cs` | same |
| 5 | `src/ZeroAlloc.Validation/Attributes/EqualAttribute.cs` | same |
| 6 | `src/ZeroAlloc.Validation/Attributes/ExclusiveBetweenAttribute.cs` | same |
| 7 | `src/ZeroAlloc.Validation/Attributes/GreaterThanAttribute.cs` | same |
| 8 | `src/ZeroAlloc.Validation/Attributes/GreaterThanOrEqualToAttribute.cs` | same |
| 9 | `src/ZeroAlloc.Validation/Attributes/InclusiveBetweenAttribute.cs` | same |
| 10 | `src/ZeroAlloc.Validation/Attributes/IsEnumNameAttribute.cs` | same |
| 11 | `src/ZeroAlloc.Validation/Attributes/IsInEnumAttribute.cs` | same |
| 12 | `src/ZeroAlloc.Validation/Attributes/LengthAttribute.cs` | same |
| 13 | `src/ZeroAlloc.Validation/Attributes/LessThanAttribute.cs` | same |
| 14 | `src/ZeroAlloc.Validation/Attributes/LessThanOrEqualToAttribute.cs` | same |
| 15 | `src/ZeroAlloc.Validation/Attributes/MatchesAttribute.cs` | same |
| 16 | `src/ZeroAlloc.Validation/Attributes/MaxLengthAttribute.cs` | same |
| 17 | `src/ZeroAlloc.Validation/Attributes/MinLengthAttribute.cs` | same |
| 18 | `src/ZeroAlloc.Validation/Attributes/MustAttribute.cs` | same |
| 19 | `src/ZeroAlloc.Validation/Attributes/NotEmptyAttribute.cs` | same |
| 20 | `src/ZeroAlloc.Validation/Attributes/NotEqualAttribute.cs` | same |
| 21 | `src/ZeroAlloc.Validation/Attributes/NotNullAttribute.cs` | same |
| 22 | `src/ZeroAlloc.Validation/Attributes/NullAttribute.cs` | same |
| 23 | `src/ZeroAlloc.Validation/Attributes/PrecisionScaleAttribute.cs` | same |
| 24 | `src/ZeroAlloc.Validation/Attributes/SkipWhenAttribute.cs` | same |
| 25 | `src/ZeroAlloc.Validation/Attributes/StopOnFirstFailureAttribute.cs` | same |
| 26 | `src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs` | same |
| 27 | `src/ZeroAlloc.Validation/Attributes/ValidateWithAttribute.cs` | same |
| 28 | `src/ZeroAlloc.Validation/Attributes/ValidationAttribute.cs` | same |
| 29 | `src/ZeroAlloc.Validation/Core/Severity.cs` | `namespace ZeroAlloc.Validation` → `namespace ZeroAlloc.Validation` |
| 30 | `src/ZeroAlloc.Validation/Core/ValidationContext.cs` | same |
| 31 | `src/ZeroAlloc.Validation/Core/ValidationFailure.cs` | same |
| 32 | `src/ZeroAlloc.Validation/Core/ValidationResult.cs` | same |
| 33 | `src/ZeroAlloc.Validation/Core/ValidatorFor.cs` | same |
| 34 | `src/ZeroAlloc.Validation/Internal/DecimalValidator.cs` | `namespace ZeroAlloc.Validation.Internal` → `namespace ZeroAlloc.Validation.Internal` |
| 35 | `src/ZeroAlloc.Validation/Internal/EmailValidator.cs` | same |
| 36 | `src/ZeroAlloc.Validation/Internal/FailureBuffer.cs` | same |
| 37 | `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` | `namespace ZeroAlloc.Validation.Generator` → `namespace ZeroAlloc.Validation.Generator` + all FQN strings (28 constants + 16 code-gen strings) |
| 38 | `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` | `namespace ZeroAlloc.Validation.Generator` → `namespace ZeroAlloc.Validation.Generator` + 7 FQN/string changes + 3 diagnostic category strings |
| 39 | `src/ZeroAlloc.Validation.Testing/ValidationAssert.cs` | `namespace ZeroAlloc.Validation.Testing` → `namespace ZeroAlloc.Validation.Testing` |
| 40 | `src/ZeroAlloc.Validation.Testing/ValidationAssertException.cs` | same |
| 41 | `src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs` | `namespace ZeroAlloc.Validation.AspNetCore.Generator` → `namespace ZeroAlloc.Validation.AspNetCore.Generator` + FQN string line 11 + emitted type ref line 83 |

### Group B — Project Files (Breaking)

| # | File | Change |
|---|---|---|
| 42 | `src/ZeroAlloc.Validation/ZeroAlloc.Validation.csproj` | rename to `ZeroAlloc.Validation.csproj`; update ProjectReference path for Generator |
| 43 | `src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj` | rename to `ZeroAlloc.Validation.Generator.csproj` |
| 44 | `src/ZeroAlloc.Validation.Testing/ZeroAlloc.Validation.Testing.csproj` | rename to `ZeroAlloc.Validation.Testing.csproj`; update ProjectReference path |
| 45 | `src/ZeroAlloc.Validation.AspNetCore/ZeroAlloc.Validation.AspNetCore.csproj` | rename to `ZeroAlloc.Validation.AspNetCore.csproj`; update 2 ProjectReference paths |
| 46 | `src/ZeroAlloc.Validation.AspNetCore.Generator/ZeroAlloc.Validation.AspNetCore.Generator.csproj` | rename to `ZeroAlloc.Validation.AspNetCore.Generator.csproj` |
| 47 | `tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj` | rename to `ZeroAlloc.Validation.Tests.csproj`; update 4 ProjectReference paths |
| 48 | `tests/ZeroAlloc.Validation.Tests.AspNetCore/ZeroAlloc.Validation.Tests.AspNetCore.csproj` | rename to `ZeroAlloc.Validation.Tests.AspNetCore.csproj`; update 4 ProjectReference paths |
| 49 | `tests/ZeroAlloc.Validation.Tests.MSTest/ZeroAlloc.Validation.Tests.MSTest.csproj` | rename to `ZeroAlloc.Validation.Tests.MSTest.csproj`; update 2 ProjectReference paths |
| 50 | `tests/ZeroAlloc.Validation.Tests.NUnit/ZeroAlloc.Validation.Tests.NUnit.csproj` | rename to `ZeroAlloc.Validation.Tests.NUnit.csproj`; update 2 ProjectReference paths |

### Group C — Solution File (Breaking)

| # | File | Change |
|---|---|---|
| 51 | `ZeroAlloc.Validation.slnx` | rename to `ZeroAlloc.Validation.slnx`; update all 9 project paths |

### Group D — Analyzer Config (Update Required)

| # | File | Change |
|---|---|---|
| 52 | `src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md` | Category `ZeroAlloc.Validation` → `ZeroAlloc.Validation` on 3 lines |

### Group E — Test Source Files (Breaking — using statements + namespace)

All 66 test .cs files across 4 test projects. The changes per file are:
- `namespace ZeroAlloc.Validation.Tests.*` → `namespace ZeroAlloc.Validation.Tests.*`
- `using ZeroAlloc.Validation;` → `using ZeroAlloc.Validation;`
- `using ZeroAlloc.Validation.Testing;` → `using ZeroAlloc.Validation.Testing;`
- `using ZeroAlloc.Validation.Generator;` → `using ZeroAlloc.Validation.Generator;` (2 files)

| # | File |
|---|---|
| 53–118 | All .cs files in `tests/ZeroAlloc.Validation.Tests/` (55 files) |
| 119–122 | All .cs files in `tests/ZeroAlloc.Validation.Tests.AspNetCore/` (4 files) |
| 123–125 | All .cs files in `tests/ZeroAlloc.Validation.Tests.MSTest/` (~3 files) |
| 126–128 | All .cs files in `tests/ZeroAlloc.Validation.Tests.NUnit/` (~3 files) |

### Group F — Documentation (Update Required)

| # | File |
|---|---|
| 129 | `docs/features.md` |
| 130–137+ | All 24 `docs/plans/*.md` files containing "ZeroAlloc.Validation" |

---

## Risk Register

| # | Risk | Affected Files | Severity | Mitigation |
|---|---|---|---|---|
| 1 | **Generator FQN mismatch** — if `RuleEmitter.cs` FQN strings are not updated, generated validators emit `global::ZeroAlloc.Validation.X` which no longer exists, causing compile errors in all consuming projects | `RuleEmitter.cs`, `ValidatorGenerator.cs`, `AspNetCoreFilterEmitter.cs` | High | Update all FQN constants and code-gen strings atomically with the namespace rename; run full test suite as verification |
| 2 | **Internal namespace emitted code** — `FailureBuffer`, `EmailValidator`, `DecimalValidator` are referenced as `global::ZeroAlloc.Validation.Internal.X` in generated code; the new namespace `ZeroAlloc.Validation.Internal` changes the FQN | `RuleEmitter.cs` lines 116, 625, 637; `FailureBuffer.cs`, `EmailValidator.cs`, `DecimalValidator.cs` | High | Update both the namespace declarations in `.cs` files AND the emitted string literals in `RuleEmitter.cs` together |
| 3 | **`using ZeroAlloc.Validation.Internal` test helper** — generator emission tests that compile source snippets inline `using ZeroAlloc.Validation;` in test sources; these helper strings must also be updated | `GeneratorRuleEmissionTests.cs`, `GeneratorDiagnosticTests.cs` | Medium | Search for inline `using ZeroAlloc.Validation` strings inside test sources embedded in string literals; update alongside other test files |
| 4 | **Doc files** — 24 markdown docs contain code examples with `namespace ZeroAlloc.Validation;`, `using ZeroAlloc.Validation;`, project names, etc. | All files in `docs/plans/`, `docs/features.md` | Low | Bulk find-replace is safe since docs are not compiled; verify manually on `features.md` |

---

## Execution Order

### Group 1: Directory and file renames (atomic, do together)
Use `git mv` for all renames so git tracks history correctly.

```bash
# Source directories
git mv src/ZeroAlloc.Validation src/ZeroAlloc.Validation
git mv src/ZeroAlloc.Validation.Generator src/ZeroAlloc.Validation.Generator
git mv src/ZeroAlloc.Validation.Testing src/ZeroAlloc.Validation.Testing
git mv src/ZeroAlloc.Validation.AspNetCore src/ZeroAlloc.Validation.AspNetCore
git mv src/ZeroAlloc.Validation.AspNetCore.Generator src/ZeroAlloc.Validation.AspNetCore.Generator

# Test directories
git mv tests/ZeroAlloc.Validation.Tests tests/ZeroAlloc.Validation.Tests
git mv tests/ZeroAlloc.Validation.Tests.AspNetCore tests/ZeroAlloc.Validation.Tests.AspNetCore
git mv tests/ZeroAlloc.Validation.Tests.MSTest tests/ZeroAlloc.Validation.Tests.MSTest
git mv tests/ZeroAlloc.Validation.Tests.NUnit tests/ZeroAlloc.Validation.Tests.NUnit

# .csproj files (now inside new dirs)
git mv src/ZeroAlloc.Validation/ZeroAlloc.Validation.csproj src/ZeroAlloc.Validation/ZeroAlloc.Validation.csproj
git mv src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj
git mv src/ZeroAlloc.Validation.Testing/ZeroAlloc.Validation.Testing.csproj src/ZeroAlloc.Validation.Testing/ZeroAlloc.Validation.Testing.csproj
git mv src/ZeroAlloc.Validation.AspNetCore/ZeroAlloc.Validation.AspNetCore.csproj src/ZeroAlloc.Validation.AspNetCore/ZeroAlloc.Validation.AspNetCore.csproj
git mv "src/ZeroAlloc.Validation.AspNetCore.Generator/ZeroAlloc.Validation.AspNetCore.Generator.csproj" "src/ZeroAlloc.Validation.AspNetCore.Generator/ZeroAlloc.Validation.AspNetCore.Generator.csproj"
git mv tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
git mv "tests/ZeroAlloc.Validation.Tests.AspNetCore/ZeroAlloc.Validation.Tests.AspNetCore.csproj" "tests/ZeroAlloc.Validation.Tests.AspNetCore/ZeroAlloc.Validation.Tests.AspNetCore.csproj"
git mv "tests/ZeroAlloc.Validation.Tests.MSTest/ZeroAlloc.Validation.Tests.MSTest.csproj" "tests/ZeroAlloc.Validation.Tests.MSTest/ZeroAlloc.Validation.Tests.MSTest.csproj"
git mv "tests/ZeroAlloc.Validation.Tests.NUnit/ZeroAlloc.Validation.Tests.NUnit.csproj" "tests/ZeroAlloc.Validation.Tests.NUnit/ZeroAlloc.Validation.Tests.NUnit.csproj"

# Solution file
git mv ZeroAlloc.Validation.slnx ZeroAlloc.Validation.slnx
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
- `namespace ZeroAlloc.Validation` → `namespace ZeroAlloc.Validation`
- `namespace ZeroAlloc.Validation.Internal` → `namespace ZeroAlloc.Validation.Internal`

**Checkpoint:** `dotnet build` after this group. Test files still broken (using stale names) but source compiles.

### Group 5: Generator FQN strings (critical — atomic with Group 4)
Update `RuleEmitter.cs`, `ValidatorGenerator.cs`, `AspNetCoreFilterEmitter.cs`:
- All 28 FQN constants (`"ZeroAlloc.Validation.X"` → `"ZeroAlloc.Validation.X"`)
- All 16+ code-gen emitted strings (`global::ZeroAlloc.Validation.X` → `global::ZeroAlloc.Validation.X`)
- 3 internal FQN strings (`ZeroAlloc.Validation.Internal.X` → `ZeroAlloc.Validation.Internal.X`)
- 3 diagnostic category strings (`"ZeroAlloc.Validation"` → `"ZeroAlloc.Validation"`)
- `AnalyzerReleases.Unshipped.md` categories

**Checkpoint:** `dotnet build src/` — source + generator fully compiles.

### Group 6: Test files (using statements + namespaces)
All 66 test `.cs` files across 4 test projects. Bulk find-replace:
- `using ZeroAlloc.Validation;` → `using ZeroAlloc.Validation;`
- `using ZeroAlloc.Validation.Testing;` → `using ZeroAlloc.Validation.Testing;`
- `using ZeroAlloc.Validation.Generator;` → `using ZeroAlloc.Validation.Generator;`
- `namespace ZeroAlloc.Validation.Tests` → `namespace ZeroAlloc.Validation.Tests`

**Also check:** Inline source strings in generator emission tests (e.g., `"using ZeroAlloc.Validation;"` embedded in test string literals) — these must also be updated.

**Checkpoint:** `dotnet test` — all 243 tests must pass.

### Group 7: Documentation
Bulk find-replace in all 24+ `.md` files:
- `ZeroAlloc.Validation` → `ZeroAlloc.Validation`
- `ZeroAlloc.Validation.Internal` → `ZeroAlloc.Validation.Internal`

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
