# ZeroAlloc-Net Org Documentation Uniformization — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Apply consistent YAML frontmatter, standard page set, and uniform README structure across ZeroAlloc.Mediator, ZeroAlloc.ValueObjects, ZeroAlloc.Inject, and ZeroAlloc.Analyzers.

**Architecture:** Four independent repos — each gets a `docs/uniformize` branch, all changes committed per repo, then a PR opened against `main`. Tasks 1–4 are fully independent and can be dispatched in parallel. Each task reads existing source/docs for accuracy when creating new pages.

**Tech Stack:** Plain Markdown, YAML frontmatter (id/title/slug/description/sidebar_position), Mermaid diagrams (existing), `gh` CLI for PRs.

**Reference — ZValidation README template:**
Every README must follow this order:
1. Badge row (NuGet, build, license)
2. Pitch (2–3 sentences)
3. `dotnet add package` install snippet (bash block)
4. 30-second annotated example (define → call → check result)
5. Performance teaser table (valid path only) + link to `docs/performance.md`
6. Feature bullet list
7. Documentation links table (every page in `docs/`)

**Reference — YAML frontmatter template (all doc files):**
```yaml
---
id: kebab-case-id
title: Human Readable Title
slug: /docs/slug-path
description: One-line summary for sidebar previews.
sidebar_position: N
---
```
Root/landing page always uses `slug: /`.

---

## Task 1: ZeroAlloc.Mediator

**Repo path:** `C:/Projects/Prive/ZMediator`
**Remote:** `git@github.com:ZeroAlloc-Net/ZeroAlloc.Mediator.git`

### Step 1: Create branch
```bash
git -C C:/Projects/Prive/ZMediator checkout main
git -C C:/Projects/Prive/ZMediator pull
git -C C:/Projects/Prive/ZMediator checkout -b docs/uniformize
```

### Step 2: Rename numbered files to plain names

```bash
cd C:/Projects/Prive/ZMediator/docs
git mv 01-getting-started.md getting-started.md
git mv 02-requests.md requests.md
git mv 03-notifications.md notifications.md
git mv 04-streaming.md streaming.md
git mv 05-pipeline-behaviors.md pipeline-behaviors.md
git mv 06-dependency-injection.md dependency-injection.md
git mv 07-diagnostics.md diagnostics.md
git mv 08-performance.md performance.md
```

Update all internal cross-links in the renamed files and in `docs/README.md` (search for `01-`, `02-`, etc.).

Delete the draft file:
```bash
git rm C:/Projects/Prive/ZMediator/docs/pre-push-review-2026-03-16-1530.md
```

### Step 3: Add frontmatter to all existing doc files

Add these exact frontmatter blocks to the top of each file (before the existing H1):

**getting-started.md:**
```yaml
---
id: getting-started
title: Getting Started
slug: /
description: Install ZeroAlloc.Mediator and send your first request in under five minutes.
sidebar_position: 1
---
```

**requests.md:**
```yaml
---
id: requests
title: Requests & Handlers
slug: /docs/requests
description: Commands, queries, and Unit responses — the request/response core of ZeroAlloc.Mediator.
sidebar_position: 2
---
```

**notifications.md:**
```yaml
---
id: notifications
title: Notifications
slug: /docs/notifications
description: Publish domain events to multiple handlers with sequential, parallel, or polymorphic dispatch.
sidebar_position: 3
---
```

**streaming.md:**
```yaml
---
id: streaming
title: Streaming
slug: /docs/streaming
description: Return IAsyncEnumerable<T> from a handler for large or live result sets.
sidebar_position: 4
---
```

**pipeline-behaviors.md:**
```yaml
---
id: pipeline-behaviors
title: Pipeline Behaviors
slug: /docs/pipeline-behaviors
description: Compile-time middleware for logging, validation, caching, and other cross-cutting concerns.
sidebar_position: 5
---
```

**dependency-injection.md:**
```yaml
---
id: dependency-injection
title: Dependency Injection
slug: /docs/dependency-injection
description: Three handler instantiation modes — factory delegates, IMediator, and static Mediator.
sidebar_position: 6
---
```

**diagnostics.md:**
```yaml
---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZAM001–ZAM007 Roslyn analyzer rules with triggers, severities, and fix guidance.
sidebar_position: 7
---
```

**performance.md:**
```yaml
---
id: performance
title: Performance
slug: /docs/performance
description: Zero-allocation design decisions and benchmark results against MediatR.
sidebar_position: 8
---
```

### Step 4: Create `docs/advanced.md`

Read the existing docs (especially pipeline-behaviors.md and dependency-injection.md) and the source in `C:/Projects/Prive/ZMediator/src/` to understand what advanced patterns exist. Create `C:/Projects/Prive/ZMediator/docs/advanced.md` covering:

- Error handling — what happens when a handler throws; exception propagation
- Cancellation — passing CancellationToken through request dispatch
- Combining features — pipeline behavior + streaming; notification + pipeline
- Scoped behaviors — behaviors that depend on scoped DI services
- Multiple handlers for the same request (if supported)

Frontmatter:
```yaml
---
id: advanced
title: Advanced Patterns
slug: /docs/advanced
description: Error handling, cancellation, scoped behaviors, and combining Mediator features.
sidebar_position: 9
---
```

### Step 5: Create `docs/testing.md`

Read the test projects in `C:/Projects/Prive/ZMediator/tests/` to understand how handlers are tested. Create `C:/Projects/Prive/ZMediator/docs/testing.md` covering:

- Testing a request handler directly (instantiate + call Handle)
- Testing with a real mediator instance
- Mocking IMediator in ASP.NET Core controller tests
- Testing pipeline behaviors in isolation
- Testing notifications (verifying all handlers were called)

Frontmatter:
```yaml
---
id: testing
title: Testing
slug: /docs/testing
description: Unit-test request handlers, notifications, and pipeline behaviors.
sidebar_position: 10
---
```

### Step 6: Update `README.md`

Read the current `C:/Projects/Prive/ZMediator/README.md`. Verify it follows the standard template:
1. Badge row — NuGet, build, license (already present — verify all three)
2. Pitch — 2–3 sentences (already present — verify)
3. `dotnet add package ZeroAlloc.Mediator` bash install snippet
4. 30-second annotated example showing a request + handler + dispatch call
5. Performance teaser (condensed valid-path table, link to `docs/performance.md`)
6. Feature list
7. **Update docs links table** to use the new plain filenames (remove `01-` etc.)

### Step 7: Commit and push

```bash
git -C C:/Projects/Prive/ZMediator add -A
git -C C:/Projects/Prive/ZMediator commit -m "docs: uniformize documentation structure and frontmatter"
git -C C:/Projects/Prive/ZMediator push -u origin docs/uniformize
```

### Step 8: Create PR

```bash
gh pr create --repo ZeroAlloc-Net/ZeroAlloc.Mediator \
  --title "docs: uniformize documentation structure and frontmatter" \
  --body "$(cat <<'EOF'
## Summary

- Add full YAML frontmatter (id, title, slug, description, sidebar_position) to all doc pages
- Rename numbered files (01-getting-started.md → getting-started.md etc.) — ordering now via sidebar_position
- Add missing pages: advanced.md (error handling, cancellation, scoped behaviors) and testing.md
- Update README to align with org-wide template (badges, pitch, install, example, performance, features, links)
- Delete draft pre-push-review file from docs/

## Test Plan
- [ ] All doc pages have complete frontmatter
- [ ] No broken internal links after renames
- [ ] New pages are accurate against source code
- [ ] README follows standard structure
EOF
)"
```

---

## Task 2: ZeroAlloc.ValueObjects

**Repo path:** `C:/Projects/Prive/ZeroAlloc.ValueObjects`
**Remote:** `https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects.git`

### Step 1: Create branch
```bash
git -C C:/Projects/Prive/ZeroAlloc.ValueObjects checkout main
git -C C:/Projects/Prive/ZeroAlloc.ValueObjects pull
git -C C:/Projects/Prive/ZeroAlloc.ValueObjects checkout -b docs/uniformize
```

### Step 2: Add frontmatter to all existing doc files

**index.md** (root landing page):
```yaml
---
id: index
title: ZeroAlloc.ValueObjects
slug: /
description: Zero-allocation source-generated value object equality for .NET — no boxing, no iterator allocations.
sidebar_position: 1
---
```

**why.md:**
```yaml
---
id: why
title: Why This Library Exists
slug: /docs/why
description: The allocation problem with CSharpFunctionalExtensions.ValueObject and how ZeroAlloc solves it.
sidebar_position: 2
---
```

**getting-started.md:**
```yaml
---
id: getting-started
title: Getting Started
slug: /docs/getting-started
description: Install, annotate a class, and get zero-allocation equality in three steps.
sidebar_position: 3
---
```

**installation.md:**
```yaml
---
id: installation
title: Installation
slug: /docs/installation
description: NuGet setup and .NET version requirements for ZeroAlloc.ValueObjects.
sidebar_position: 4
---
```

**attributes.md:**
```yaml
---
id: attributes
title: Attribute Reference
slug: /docs/attributes
description: Complete reference for [ValueObject], [EqualityMember], and [IgnoreEqualityMember].
sidebar_position: 5
---
```

**member-selection.md:**
```yaml
---
id: member-selection
title: Member Selection
slug: /docs/member-selection
description: How the generator decides which properties participate in equality — default, opt-in, and opt-out modes.
sidebar_position: 6
---
```

**generated-output.md:**
```yaml
---
id: generated-output
title: Generated Output
slug: /docs/generated-output
description: Exact code the source generator emits for Equals, GetHashCode, and ToString.
sidebar_position: 7
---
```

**nullable-properties.md:**
```yaml
---
id: nullable-properties
title: Nullable Properties
slug: /docs/nullable-properties
description: Null-safe equality comparison for nullable reference type members.
sidebar_position: 8
---
```

**struct-vs-class.md:**
```yaml
---
id: struct-vs-class
title: Struct vs. Class
slug: /docs/struct-vs-class
description: When to use a struct value object and when to use a class — trade-offs and decision guide.
sidebar_position: 9
---
```

**patterns.md:**
```yaml
---
id: patterns
title: Usage Patterns
slug: /docs/patterns
description: Dictionary keys, HashSets, LINQ, EF Core, and other common value object scenarios.
sidebar_position: 10
---
```

**migration.md:**
```yaml
---
id: migration
title: Migration Guide
slug: /docs/migration
description: Step-by-step guide for migrating from CSharpFunctionalExtensions.ValueObject.
sidebar_position: 11
---
```

**performance.md:**
```yaml
---
id: performance
title: Performance
slug: /docs/performance
description: Benchmark results comparing ZeroAlloc.ValueObjects against record, record struct, and CSharpFunctionalExtensions.
sidebar_position: 12
---
```

**design.md:**
```yaml
---
id: design
title: Design Decisions
slug: /docs/design
description: Intentional omissions (no with, no Deconstruct, no IComparable) and the reasoning behind them.
sidebar_position: 13
---
```

**troubleshooting.md:**
```yaml
---
id: troubleshooting
title: Troubleshooting
slug: /docs/troubleshooting
description: Solutions for common errors including "type must be partial" and EF Core integration issues.
sidebar_position: 14
---
```

**testing.md** (NEW — create this file):
```yaml
---
id: testing
title: Testing
slug: /docs/testing
description: Write unit tests for value object equality, dictionary usage, and validation.
sidebar_position: 15
---
```
Content to cover (read source in `C:/Projects/Prive/ZeroAlloc.ValueObjects/src/` and `tests/` for accuracy):
- Asserting equality between two instances
- Asserting inequality
- Using value objects as dictionary keys in tests
- Testing with xUnit, NUnit, MSTest (no special helpers needed — standard == works)
- Testing with nullable value objects
- Common assertion patterns

**examples/ecommerce.md:**
```yaml
---
id: examples-ecommerce
title: E-Commerce Examples
slug: /docs/examples/ecommerce
description: ProductId, OrderId, and Money value objects for e-commerce domains.
sidebar_position: 16
---
```

**examples/finance.md:**
```yaml
---
id: examples-finance
title: Finance Examples
slug: /docs/examples/finance
description: IBAN, Currency, and Amount value objects for financial domains.
sidebar_position: 17
---
```

**examples/geospatial.md:**
```yaml
---
id: examples-geospatial
title: Geospatial Examples
slug: /docs/examples/geospatial
description: Coordinates, BoundingBox, and GeoHash value objects for geospatial domains.
sidebar_position: 18
---
```

**examples/hr-identity.md:**
```yaml
---
id: examples-hr-identity
title: HR & Identity Examples
slug: /docs/examples/hr-identity
description: EmailAddress, EmployeeId, and Department value objects for HR and identity domains.
sidebar_position: 19
---
```

**examples/scheduling.md:**
```yaml
---
id: examples-scheduling
title: Scheduling Examples
slug: /docs/examples/scheduling
description: DateRange, TimeSlot, and RecurrencePattern value objects for scheduling domains.
sidebar_position: 20
---
```

### Step 3: Update `README.md`

Verify/align to standard template. The README already has strong content — check:
- Badges present (add if missing)
- Install uses bash block (`dotnet add package`)
- Example shows define → call → equality check
- Performance table present with link to docs
- Feature list present
- Docs links table updated to match all pages above

### Step 4: Commit and push

```bash
git -C C:/Projects/Prive/ZeroAlloc.ValueObjects add -A
git -C C:/Projects/Prive/ZeroAlloc.ValueObjects commit -m "docs: uniformize documentation structure and frontmatter"
git -C C:/Projects/Prive/ZeroAlloc.ValueObjects push -u origin docs/uniformize
```

### Step 5: Create PR

```bash
gh pr create --repo ZeroAlloc-Net/ZeroAlloc.ValueObjects \
  --title "docs: uniformize documentation structure and frontmatter" \
  --body "$(cat <<'EOF'
## Summary

- Add full YAML frontmatter (id, title, slug, description, sidebar_position) to all 19 doc pages
- Create missing testing.md page
- Align README to org-wide template

## Test Plan
- [ ] All doc pages have complete frontmatter
- [ ] No broken internal links
- [ ] testing.md is accurate against source code
- [ ] README follows standard structure
EOF
)"
```

---

## Task 3: ZeroAlloc.Inject

**Repo path:** `C:/Projects/Prive/ZeroAlloc.Inject`
**Remote:** `https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject.git`

### Step 1: Create branch
```bash
git -C C:/Projects/Prive/ZeroAlloc.Inject checkout main
git -C C:/Projects/Prive/ZeroAlloc.Inject pull
git -C C:/Projects/Prive/ZeroAlloc.Inject checkout -b docs/uniformize
```

### Step 2: Flatten `reference/` subfolder

```bash
git -C C:/Projects/Prive/ZeroAlloc.Inject mv docs/reference/benchmarks.md docs/performance.md
git -C C:/Projects/Prive/ZeroAlloc.Inject mv docs/reference/diagnostics.md docs/diagnostics.md
rmdir C:/Projects/Prive/ZeroAlloc.Inject/docs/reference
```

### Step 3: Add frontmatter to all doc files

**getting-started.md** (already has `slug: /` — replace with full frontmatter):
```yaml
---
id: getting-started
title: Getting Started
slug: /
description: Install ZeroAlloc.Inject and register your first service with a lifetime attribute in two minutes.
sidebar_position: 1
---
```

**service-registration.md:**
```yaml
---
id: service-registration
title: Service Registration
slug: /docs/service-registration
description: Lifetime attributes, default discovery, As() narrowing, keyed services, and open generics.
sidebar_position: 2
---
```

**container-modes.md:**
```yaml
---
id: container-modes
title: Container Modes
slug: /docs/container-modes
description: Choose between MS DI Extension, Hybrid, and Standalone container modes.
sidebar_position: 3
---
```

**decorators.md:**
```yaml
---
id: decorators
title: Decorators
slug: /docs/decorators
description: Apply the decorator pattern at compile time with the [Decorator] attribute.
sidebar_position: 4
---
```

**native-aot.md:**
```yaml
---
id: native-aot
title: Native AOT
slug: /docs/native-aot
description: Why ZeroAlloc.Inject is trimmer-safe and how to publish a Native AOT application.
sidebar_position: 5
---
```

**advanced.md:**
```yaml
---
id: advanced
title: Advanced Patterns
slug: /docs/advanced
description: Multi-assembly setup, constructor disambiguation, collection injection, and custom method names.
sidebar_position: 6
---
```

**diagnostics.md** (moved from reference/):
```yaml
---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZAI001–ZAI017 Roslyn analyzer rules with triggers, severities, and fix guidance.
sidebar_position: 7
---
```

**performance.md** (moved from reference/benchmarks.md):
```yaml
---
id: performance
title: Performance
slug: /docs/performance
description: Startup time and resolution benchmarks for MS DI Extension, Hybrid, and Standalone modes.
sidebar_position: 8
---
```

### Step 4: Create `docs/testing.md`

Read the test projects in `C:/Projects/Prive/ZeroAlloc.Inject/tests/` to understand how DI-registered classes are tested. Create `C:/Projects/Prive/ZeroAlloc.Inject/docs/testing.md` covering:

- Testing services registered with ZeroAlloc.Inject in isolation (no DI needed)
- Setting up a test service collection with `AddZeroAllocServices()`
- Resolving services in integration tests
- Testing with different container modes (MS DI Extension vs Standalone)
- Verifying DI registration (asserting a service is registered)

Frontmatter:
```yaml
---
id: testing
title: Testing
slug: /docs/testing
description: Test DI-registered services in isolation and with a real container in integration tests.
sidebar_position: 9
---
```

### Step 5: Update `README.md`

Read `C:/Projects/Prive/ZeroAlloc.Inject/README.md`. Verify/align to standard template. The existing README is strong — check:
- Three `dotnet add package` options shown (base, bundle, generator separately)
- Example shows `[Transient]` annotation + `AddZeroAllocServices()` + resolve
- Performance table present and links to `docs/performance.md`
- Docs links table updated (remove `reference/` paths, use new flat paths)

### Step 6: Commit and push

```bash
git -C C:/Projects/Prive/ZeroAlloc.Inject add -A
git -C C:/Projects/Prive/ZeroAlloc.Inject commit -m "docs: uniformize documentation structure and frontmatter"
git -C C:/Projects/Prive/ZeroAlloc.Inject push -u origin docs/uniformize
```

### Step 7: Create PR

```bash
gh pr create --repo ZeroAlloc-Net/ZeroAlloc.Inject \
  --title "docs: uniformize documentation structure and frontmatter" \
  --body "$(cat <<'EOF'
## Summary

- Add full YAML frontmatter to all doc pages
- Flatten reference/ subfolder: benchmarks.md → performance.md, diagnostics.md → diagnostics.md
- Create missing testing.md page
- Align README to org-wide template

## Test Plan
- [ ] All doc pages have complete frontmatter
- [ ] reference/ links in README and existing docs updated to flat paths
- [ ] testing.md is accurate against source code
- [ ] README follows standard structure
EOF
)"
```

---

## Task 4: ZeroAlloc.Analyzers

**Repo path:** `C:/Projects/Prive/ZeroAlloc` (note: local folder is `ZeroAlloc`, not `ZeroAlloc.Analyzers`)
**Remote:** `https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers.git`

### Step 1: Create branch
```bash
git -C C:/Projects/Prive/ZeroAlloc checkout main
git -C C:/Projects/Prive/ZeroAlloc pull
git -C C:/Projects/Prive/ZeroAlloc checkout -b docs/uniformize
```

### Step 2: Add frontmatter to main doc files

**getting-started.md:**
```yaml
---
id: getting-started
title: Getting Started
slug: /
description: Install ZeroAlloc.Analyzers and start catching allocation-heavy patterns at compile time.
sidebar_position: 1
---
```

**configuration.md:**
```yaml
---
id: configuration
title: Configuration
slug: /docs/configuration
description: Tune analyzer severities, suppress individual rules, and configure TFM-aware gating.
sidebar_position: 2
---
```

### Step 3: Add frontmatter to all `rules/` files

**rules/collections.md:**
```yaml
---
id: rules-collections
title: Collections Rules (ZA01xx)
slug: /docs/rules/collections
description: FrozenDictionary, FrozenSet, and collection type selection rules.
sidebar_position: 3
---
```

**rules/strings.md:**
```yaml
---
id: rules-strings
title: Strings Rules (ZA02xx)
slug: /docs/rules/strings
description: StringBuilder, AsSpan, and string.Create allocation pattern rules.
sidebar_position: 4
---
```

**rules/memory.md:**
```yaml
---
id: rules-memory
title: Memory Rules (ZA03xx)
slug: /docs/rules/memory
description: Stackalloc vs ArrayPool rules for temporary buffer allocation.
sidebar_position: 5
---
```

**rules/logging.md:**
```yaml
---
id: rules-logging
title: Logging Rules (ZA04xx)
slug: /docs/rules/logging
description: LoggerMessage source generator vs reflection-based logging rules.
sidebar_position: 6
---
```

**rules/boxing.md:**
```yaml
---
id: rules-boxing
title: Boxing Rules (ZA05xx)
slug: /docs/rules/boxing
description: Value type boxing and closure allocation pattern detection rules.
sidebar_position: 7
---
```

**rules/linq.md:**
```yaml
---
id: rules-linq
title: LINQ Rules (ZA06xx)
slug: /docs/rules/linq
description: Iterator allocation and lazy pipeline traversal overhead rules.
sidebar_position: 8
---
```

**rules/regex.md:**
```yaml
---
id: rules-regex
title: Regex Rules (ZA07xx)
slug: /docs/rules/regex
description: GeneratedRegex source generator vs runtime regex compilation rules.
sidebar_position: 9
---
```

**rules/enums.md:**
```yaml
---
id: rules-enums
title: Enums Rules (ZA08xx)
slug: /docs/rules/enums
description: HasFlag boxing on pre-.NET 7 and Enum.ToString reflection rules.
sidebar_position: 10
---
```

**rules/sealing.md:**
```yaml
---
id: rules-sealing
title: Sealing Rules (ZA09xx)
slug: /docs/rules/sealing
description: Class sealing for JIT devirtualization rules.
sidebar_position: 11
---
```

**rules/serialization.md:**
```yaml
---
id: rules-serialization
title: Serialization Rules (ZA10xx)
slug: /docs/rules/serialization
description: JSON source generation vs reflection-based serialization rules.
sidebar_position: 12
---
```

**rules/async.md:**
```yaml
---
id: rules-async
title: Async Rules (ZA11xx)
slug: /docs/rules/async
description: Async state machine allocation and CancellationTokenSource leak rules.
sidebar_position: 13
---
```

**rules/delegates.md:**
```yaml
---
id: rules-delegates
title: Delegates Rules (ZA14xx)
slug: /docs/rules/delegates
description: Static lambda caching and closure elimination rules.
sidebar_position: 14
---
```

**rules/value-types.md:**
```yaml
---
id: rules-value-types
title: Value Types Rules (ZA15xx)
slug: /docs/rules/value-types
description: Struct GetHashCode override and finalizer overhead rules.
sidebar_position: 15
---
```

### Step 4: Create `docs/performance.md`

Read `C:/Projects/Prive/ZeroAlloc/src/` and any benchmark data to understand analyzer overhead. Create `C:/Projects/Prive/ZeroAlloc/docs/performance.md` covering:

- How Roslyn analyzers affect build time (incremental analysis, analyzer host overhead)
- ZeroAlloc.Analyzers' approach to minimizing overhead (incremental generators, caching)
- Multi-TFM builds — how TFM-aware gating avoids running inapplicable rules
- Tips for fast builds: `<EnforceCodeStyleInBuild>` setting, analyzer parallelism
- When to disable in CI vs local development

Frontmatter:
```yaml
---
id: performance
title: Build Performance
slug: /docs/performance
description: How ZeroAlloc.Analyzers minimizes build-time overhead through incremental analysis and TFM-aware rule gating.
sidebar_position: 16
---
```

### Step 5: Create `docs/testing.md`

Read the test projects in `C:/Projects/Prive/ZeroAlloc/tests/` to understand how analyzer rules are tested. Create `C:/Projects/Prive/ZeroAlloc/docs/testing.md` covering:

- How to suppress analyzer warnings in test code (`#pragma warning disable`, `[SuppressMessage]`)
- Writing tests that intentionally trigger a diagnostic (Roslyn test helpers: `Microsoft.CodeAnalysis.Testing`)
- Verifying a warning is NOT raised (no-false-positive tests)
- TFM-specific testing: confirming a rule is off on net8.0 and on for net9.0

Frontmatter:
```yaml
---
id: testing
title: Testing with Analyzers
slug: /docs/testing
description: Suppress warnings in tests, write diagnostic unit tests with Roslyn test helpers, and verify TFM-gated rules.
sidebar_position: 17
---
```

### Step 6: Update `README.md`

Read `C:/Projects/Prive/ZeroAlloc/README.md`. Verify/align to standard template:
- Add NuGet + build badges if missing
- Pitch: 2–3 sentences (what it catches, how many rules, TFM awareness)
- Install snippet in bash block
- Example: code before/after showing a rule firing and the fix
- No performance table (analyzer tool — skip this section)
- Feature list: number of rules, categories, TFM gating
- Docs links table covering all pages above

### Step 7: Commit and push

```bash
git -C C:/Projects/Prive/ZeroAlloc add -A
git -C C:/Projects/Prive/ZeroAlloc commit -m "docs: uniformize documentation structure and frontmatter"
git -C C:/Projects/Prive/ZeroAlloc push -u origin docs/uniformize
```

### Step 8: Create PR

```bash
gh pr create --repo ZeroAlloc-Net/ZeroAlloc.Analyzers \
  --title "docs: uniformize documentation structure and frontmatter" \
  --body "$(cat <<'EOF'
## Summary

- Add full YAML frontmatter to all 15 doc pages (getting-started, configuration, 13 rule category pages)
- Create missing performance.md (build-time impact and TFM-aware gating)
- Create missing testing.md (suppress warnings, Roslyn test helpers, TFM-gated rule testing)
- Align README to org-wide template

## Test Plan
- [ ] All doc pages have complete frontmatter
- [ ] All 13 rules/ pages have correct ZAxx rule codes in frontmatter
- [ ] performance.md accurately describes build overhead
- [ ] testing.md is accurate against source code
- [ ] README follows standard structure
EOF
)"
```
