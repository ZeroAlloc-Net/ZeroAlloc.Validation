# ZeroAlloc-Net Org Documentation Uniformization — Design

## Goal

Apply a consistent documentation structure and YAML frontmatter across all four
ZeroAlloc-Net library repos, matching the pattern established in ZeroAlloc.Validation.

## Repos in scope

| Repo | Local path | Branch strategy |
|---|---|---|
| ZeroAlloc.Mediator | `C:/Projects/Prive/ZMediator` | PR from `docs/uniformize` |
| ZeroAlloc.ValueObjects | `C:/Projects/Prive/ZeroAlloc.ValueObjects` | PR from `docs/uniformize` |
| ZeroAlloc.Inject | `C:/Projects/Prive/ZeroAlloc.Inject` | PR from `docs/uniformize` |
| ZeroAlloc.Analyzers | `C:/Projects/Prive/ZeroAlloc` | PR from `docs/uniformize` |

## Standard README structure (all repos)

Every `README.md` must follow this order:

1. Badge row — NuGet version, build status, license
2. Pitch — 2–3 sentences: what it is, key differentiators
3. Install — `dotnet add package` bash snippet
4. 30-second example — annotated define → call → check result code block
5. Performance teaser — condensed valid-path table + link to `docs/performance.md`
6. Feature list — bullet list of capabilities
7. Documentation links table — links to every page in `docs/`

## Standard YAML frontmatter (all doc files)

```yaml
---
id: kebab-case-unique-id
title: Human Readable Title
slug: /docs/slug
description: One-line summary for SEO and sidebar previews.
sidebar_position: N
---
```

The root/landing page for each repo uses `slug: /`.

## Standard page set

Every repo gets these pages. New content is created for missing pages by reading
source code — same approach used for ZeroAlloc.Validation.

| sidebar_position | File | All repos | Notes |
|---|---|---|---|
| 1 | `getting-started.md` | ✅ all have it | |
| 2–N | library-specific pages | varies | see per-repo below |
| N+1 | `diagnostics.md` | all | compiler diagnostics / error codes |
| N+2 | `performance.md` | Mediator, ValueObjects, Inject | benchmarks; for Analyzers: build-time impact |
| N+3 | `advanced.md` | Mediator, Inject | create for Mediator; exists for Inject |
| N+4 | `testing.md` | all | create new for all four repos |

## Per-repo changes

### ZeroAlloc.Mediator

**File renames** (number prefix → plain name, ordering via `sidebar_position`):
- `01-getting-started.md` → `getting-started.md`
- `02-requests.md` → `requests.md`
- `03-notifications.md` → `notifications.md`
- `04-streaming.md` → `streaming.md`
- `05-pipeline-behaviors.md` → `pipeline-behaviors.md`
- `06-dependency-injection.md` → `dependency-injection.md`
- `07-diagnostics.md` → `diagnostics.md`
- `08-performance.md` → `performance.md`

**New pages to create:**
- `advanced.md` — advanced patterns (error handling, cancellation, scoped behaviors, combining features)
- `testing.md` — how to unit-test handlers, how to mock the mediator

**Housekeeping:**
- Delete `pre-push-review-2026-03-16-1530.md` from `docs/`
- `docs/README.md` — update links after renames
- `cookbook/` — keep as-is (not part of main docs nav)

### ZeroAlloc.ValueObjects

**All existing files keep their names.** Add frontmatter to:
`index.md`, `why.md`, `getting-started.md`, `installation.md`, `attributes.md`,
`member-selection.md`, `generated-output.md`, `nullable-properties.md`,
`struct-vs-class.md`, `patterns.md`, `migration.md`, `performance.md`,
`design.md`, `troubleshooting.md`,
`examples/ecommerce.md`, `examples/finance.md`, `examples/geospatial.md`,
`examples/hr-identity.md`, `examples/scheduling.md`

**New pages to create:**
- `testing.md` — how to test value objects in unit tests (equality assertions, dictionary usage, xUnit/NUnit/MSTest)

### ZeroAlloc.Inject

**Flatten reference/ subfolder:**
- `reference/benchmarks.md` → `performance.md`
- `reference/diagnostics.md` → `diagnostics.md`
- Remove empty `reference/` directory

**Add frontmatter to all existing files:**
`getting-started.md`, `service-registration.md`, `container-modes.md`,
`decorators.md`, `native-aot.md`, `advanced.md`, `diagnostics.md`, `performance.md`

**New pages to create:**
- `testing.md` — how to test classes registered with ZeroAlloc.Inject (test container setup, service resolution in tests)

### ZeroAlloc.Analyzers

**Add frontmatter to:**
`getting-started.md`, `configuration.md`,
`rules/async.md`, `rules/boxing.md`, `rules/collections.md`, `rules/delegates.md`,
`rules/enums.md`, `rules/linq.md`, `rules/logging.md`, `rules/memory.md`,
`rules/regex.md`, `rules/sealing.md`, `rules/serialization.md`, `rules/strings.md`,
`rules/value-types.md`

**New pages to create:**
- `performance.md` — how Roslyn analyzers affect build time: incremental analysis, caching, multi-TFM overhead; tips for keeping builds fast
- `testing.md` — how to write unit tests for code that uses ZeroAlloc.Analyzers (suppress in tests, verify warnings, roslyn test helpers)

## Execution strategy

Four repos are independent → dispatch parallel subagents (one per repo).
Each subagent:
1. Creates branch `docs/uniformize` from `main`
2. Makes all changes (renames, frontmatter, new pages, README fixes)
3. Commits per logical group
4. Opens a PR against `main`

## Tech constraints

- Plain Markdown (CommonMark)
- YAML frontmatter on every `.md` in `docs/`
- Mermaid fenced blocks for diagrams (consistent with existing docs)
- No MDX, no React components
- New pages created by reading library source code for accuracy
