# Solution Structure Design

**Date:** 2026-03-15
**Status:** Approved

---

## Overview

ZValidation is a code-generated, zero-allocation validation library for .NET. This document describes the approved solution and project structure.

---

## Projects

| Project | TFM | Role |
|---------|-----|------|
| `ZValidation` | `net8.0;net9.0;net10.0` | Core types (`ValidatorFor<T>`, `ValidationResult`, `ValidationFailure`, `ValidationContext`). Bundles the generator as an analyzer in the NuGet package. |
| `ZValidation.Generator` | `netstandard2.0` | Roslyn incremental source generator. Reads `ValidatorFor<T>` partial classes and emits zero-alloc validation dispatch code. Shipped inside the `ZValidation` NuGet under `analyzers/dotnet/cs/`. |
| `ZValidation.AspNetCore` | `net8.0;net9.0;net10.0` | `IActionFilter` auto-validation, `ValidationProblemDetails` mapping, `IValidateOptions<T>` bridge, ZInject-generated DI registration. |
| `ZValidation.Testing` | `net8.0;net9.0;net10.0` | Framework-agnostic assertion helpers (`ValidationAssert.HasError`, `ValidationAssert.NoErrors`, etc.). No dependency on xUnit/NUnit/MSTest — uses plain exceptions so it works with any test framework. |
| `ZValidation.Tests` | `net8.0;net9.0;net10.0` | Primary test suite using **xUnit**. Tests all core behaviour, generator output, and `ZValidation.Testing` helpers. |
| `ZValidation.Tests.NUnit` | `net8.0;net9.0;net10.0` | Compatibility tests verifying `ZValidation.Testing` helpers integrate correctly with **NUnit**. |
| `ZValidation.Tests.MSTest` | `net8.0;net9.0;net10.0` | Same as above for **MSTest**. |

---

## NuGet Packages

| Package | Contains |
|---------|----------|
| `ZValidation` | Core types + bundled source generator |
| `ZValidation.AspNetCore` | ASP.NET Core integration |
| `ZValidation.Testing` | Framework-agnostic test helpers |

---

## Directory Layout

```
ZValidation.sln
├── src/
│   ├── ZValidation/
│   │   ├── ZValidation.csproj
│   │   ├── Core/                  # ValidatorFor<T>, ValidationResult, ValidationFailure, ValidationContext
│   │   └── Rules/                 # Built-in rule implementations (NotEmpty, GreaterThan, etc.)
│   ├── ZValidation.Generator/
│   │   ├── ZValidation.Generator.csproj
│   │   └── ValidatorGenerator.cs  # Incremental source generator entry point
│   ├── ZValidation.AspNetCore/
│   │   ├── ZValidation.AspNetCore.csproj
│   │   └── Integration/           # ActionFilter, ProblemDetails mapping, DI extensions
│   └── ZValidation.Testing/
│       ├── ZValidation.Testing.csproj
│       └── ValidationAssert.cs    # Framework-agnostic assertions
└── tests/
    ├── ZValidation.Tests/
    │   └── ZValidation.Tests.csproj        # xUnit
    ├── ZValidation.Tests.NUnit/
    │   └── ZValidation.Tests.NUnit.csproj  # NUnit
    └── ZValidation.Tests.MSTest/
        └── ZValidation.Tests.MSTest.csproj # MSTest
```

---

## Project Dependency Graph

```
ZValidation.Generator  ──(bundled as analyzer)──▶  ZValidation
ZValidation            ──(referenced by)──────────▶  ZValidation.AspNetCore
ZValidation            ──(referenced by)──────────▶  ZValidation.Testing
ZValidation            ──(referenced by)──────────▶  ZValidation.Tests
ZValidation.Testing    ──(referenced by)──────────▶  ZValidation.Tests
ZValidation.Testing    ──(referenced by)──────────▶  ZValidation.Tests.NUnit
ZValidation.Testing    ──(referenced by)──────────▶  ZValidation.Tests.MSTest
```

---

## Key Decisions

- **Generator stays on `netstandard2.0`** — Roslyn requires this; the consumer-facing projects target modern .NET only.
- **Multi-targeting `net8.0;net9.0;net10.0`** — ensures compatibility across all currently supported .NET versions.
- **`ZValidation.Testing` has no test framework dependency** — assertions throw plain exceptions, making helpers usable with xUnit, NUnit, MSTest, or any future framework without an adapter package.
- **One primary test project (xUnit) + two compat projects** — xUnit is the primary framework for the library's own tests; NUnit and MSTest projects exist solely to verify `ZValidation.Testing` works across all three.
