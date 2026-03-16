# Solution Structure Design

**Date:** 2026-03-15
**Status:** Approved

---

## Overview

ZeroAlloc.Validation is a code-generated, zero-allocation validation library for .NET. This document describes the approved solution and project structure.

---

## Projects

| Project | TFM | Role |
|---------|-----|------|
| `ZeroAlloc.Validation` | `net8.0;net9.0;net10.0` | Core types (`ValidatorFor<T>`, `ValidationResult`, `ValidationFailure`, `ValidationContext`). Bundles the generator as an analyzer in the NuGet package. |
| `ZeroAlloc.Validation.Generator` | `netstandard2.0` | Roslyn incremental source generator. Reads `ValidatorFor<T>` partial classes and emits zero-alloc validation dispatch code. Shipped inside the `ZeroAlloc.Validation` NuGet under `analyzers/dotnet/cs/`. |
| `ZeroAlloc.Validation.AspNetCore` | `net8.0;net9.0;net10.0` | `IActionFilter` auto-validation, `ValidationProblemDetails` mapping, `IValidateOptions<T>` bridge, ZInject-generated DI registration. |
| `ZeroAlloc.Validation.Testing` | `net8.0;net9.0;net10.0` | Framework-agnostic assertion helpers (`ValidationAssert.HasError`, `ValidationAssert.NoErrors`, etc.). No dependency on xUnit/NUnit/MSTest — uses plain exceptions so it works with any test framework. |
| `ZeroAlloc.Validation.Tests` | `net8.0;net9.0;net10.0` | Primary test suite using **xUnit**. Tests all core behaviour, generator output, and `ZeroAlloc.Validation.Testing` helpers. |
| `ZeroAlloc.Validation.Tests.NUnit` | `net8.0;net9.0;net10.0` | Compatibility tests verifying `ZeroAlloc.Validation.Testing` helpers integrate correctly with **NUnit**. |
| `ZeroAlloc.Validation.Tests.MSTest` | `net8.0;net9.0;net10.0` | Same as above for **MSTest**. |

---

## NuGet Packages

| Package | Contains |
|---------|----------|
| `ZeroAlloc.Validation` | Core types + bundled source generator |
| `ZeroAlloc.Validation.AspNetCore` | ASP.NET Core integration |
| `ZeroAlloc.Validation.Testing` | Framework-agnostic test helpers |

---

## Directory Layout

```
ZeroAlloc.Validation.sln
├── src/
│   ├── ZeroAlloc.Validation/
│   │   ├── ZeroAlloc.Validation.csproj
│   │   ├── Core/                  # ValidatorFor<T>, ValidationResult, ValidationFailure, ValidationContext
│   │   └── Rules/                 # Built-in rule implementations (NotEmpty, GreaterThan, etc.)
│   ├── ZeroAlloc.Validation.Generator/
│   │   ├── ZeroAlloc.Validation.Generator.csproj
│   │   └── ValidatorGenerator.cs  # Incremental source generator entry point
│   ├── ZeroAlloc.Validation.AspNetCore/
│   │   ├── ZeroAlloc.Validation.AspNetCore.csproj
│   │   └── Integration/           # ActionFilter, ProblemDetails mapping, DI extensions
│   └── ZeroAlloc.Validation.Testing/
│       ├── ZeroAlloc.Validation.Testing.csproj
│       └── ValidationAssert.cs    # Framework-agnostic assertions
└── tests/
    ├── ZeroAlloc.Validation.Tests/
    │   └── ZeroAlloc.Validation.Tests.csproj        # xUnit
    ├── ZeroAlloc.Validation.Tests.NUnit/
    │   └── ZeroAlloc.Validation.Tests.NUnit.csproj  # NUnit
    └── ZeroAlloc.Validation.Tests.MSTest/
        └── ZeroAlloc.Validation.Tests.MSTest.csproj # MSTest
```

---

## Project Dependency Graph

```
ZeroAlloc.Validation.Generator  ──(bundled as analyzer)──▶  ZeroAlloc.Validation
ZeroAlloc.Validation            ──(referenced by)──────────▶  ZeroAlloc.Validation.AspNetCore
ZeroAlloc.Validation            ──(referenced by)──────────▶  ZeroAlloc.Validation.Testing
ZeroAlloc.Validation            ──(referenced by)──────────▶  ZeroAlloc.Validation.Tests
ZeroAlloc.Validation.Testing    ──(referenced by)──────────▶  ZeroAlloc.Validation.Tests
ZeroAlloc.Validation.Testing    ──(referenced by)──────────▶  ZeroAlloc.Validation.Tests.NUnit
ZeroAlloc.Validation.Testing    ──(referenced by)──────────▶  ZeroAlloc.Validation.Tests.MSTest
```

---

## Key Decisions

- **Generator stays on `netstandard2.0`** — Roslyn requires this; the consumer-facing projects target modern .NET only.
- **Multi-targeting `net8.0;net9.0;net10.0`** — ensures compatibility across all currently supported .NET versions.
- **`ZeroAlloc.Validation.Testing` has no test framework dependency** — assertions throw plain exceptions, making helpers usable with xUnit, NUnit, MSTest, or any future framework without an adapter package.
- **One primary test project (xUnit) + two compat projects** — xUnit is the primary framework for the library's own tests; NUnit and MSTest projects exist solely to verify `ZeroAlloc.Validation.Testing` works across all three.
