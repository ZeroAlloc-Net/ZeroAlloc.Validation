# Documentation Design: Global README + Docusaurus Docs

## Goal

Produce a root `README.md` (GitHub landing page) and ten documentation pages under `docs/` that serve as the source content for a Docusaurus-powered public website.

## Audience

External library consumers — .NET developers evaluating or using ZeroAlloc.Validation from NuGet.

## Architecture

All documentation is plain Markdown. Every file under `docs/` carries full Docusaurus YAML frontmatter (`id`, `title`, `slug`, `description`, `sidebar_position`). GitHub renders frontmatter as a collapsed block and the rest of the Markdown normally, so the files read well in both environments.

Diagrams use Mermaid fenced code blocks (` ```mermaid `). Docusaurus supports Mermaid via the `@docusaurus/theme-mermaid` plugin; GitHub has rendered Mermaid natively since 2022.

## Deliverables

### `README.md` (repo root)

| Section | Contents |
|---|---|
| Badge row | NuGet version, build status, license |
| Pitch | 2–3 sentence description: source-generated, attribute-based, zero-allocation |
| Install | `dotnet add package` snippet |
| 30-second example | Annotated model + validator call + result check |
| Performance teaser | Condensed benchmark table (valid-path only); link to `docs/performance.md` |
| Feature list | Bullet list of capabilities |
| Links | Getting Started, full docs site |

### `docs/getting-started.md` — `slug: /docs/getting-started`, `sidebar_position: 1`

Install → add `[Validate]` to a class → call `validator.Validate()` → check `result.IsValid` and `result.Failures`. Includes a Mermaid flowchart of the generation pipeline (attribute → generator → validator class → call site).

### `docs/attributes.md` — `slug: /docs/attributes`, `sidebar_position: 2`

Full reference table of every built-in attribute with its target type, constructor parameters, default error message, and a one-liner code example. Grouped by category: strings, numbers, comparisons, enums, general.

### `docs/nested-validation.md` — `slug: /docs/nested-validation`, `sidebar_position: 3`

How `[NotNull]` triggers nested validation, how to override the nested validator with `[ValidateWith<TValidator>]`. Includes a Mermaid diagram showing the failure-accumulation flow through a parent→child model.

### `docs/collection-validation.md` — `slug: /docs/collection-validation`, `sidebar_position: 4`

Validating `List<T>`, arrays, and other `IEnumerable<T>` properties. Shows how index-prefixed property paths appear in `ValidationFailure.PropertyName`. Includes a Mermaid sequence diagram of the per-item iteration.

### `docs/custom-validation.md` — `slug: /docs/custom-validation`, `sidebar_position: 5`

Three levels of customisation:
1. `[Must(nameof(MyPredicate))]` — inline predicate method on the model
2. `[CustomValidation(typeof(MyRule))]` — reusable `IValidationRule<T>` class
3. Writing a new first-class attribute by subclassing `ValidationAttribute`

### `docs/error-messages.md` — `slug: /docs/error-messages`, `sidebar_position: 6`

Default message format (`"'PropertyName' must …"`). How `[DisplayName("…")]` overrides the property label. `ValidationFailure` structure (`PropertyName`, `ErrorMessage`, `Severity`).

### `docs/aspnetcore.md` — `slug: /docs/aspnetcore`, `sidebar_position: 7`

`AddZValidationAutoValidation()` in `Program.cs`, how `ZValidationActionFilter` intercepts requests, returning `ValidationProblemDetails` on failure. DI lifetime attributes (`[Scoped]`, `[Transient]`, `[Singleton]`). Mermaid sequence diagram: request → filter → validator → 400 or controller.

### `docs/testing.md` — `slug: /docs/testing`, `sidebar_position: 8`

`ValidationAssert.IsValid()` and `ValidationAssert.HasError()` helpers. Example test class for a flat model, nested model, and collection model. Notes on the `ZeroAlloc.Validation.Testing` NuGet package.

### `docs/performance.md` — `slug: /docs/performance`, `sidebar_position: 9`

Why zero allocation on the valid path: lazy-allocation pattern in the generator (buffer only created on first failure; valid returns `Array.Empty`). Full benchmark results table for flat/nested/collection across valid/invalid paths. Environment info (BenchmarkDotNet, .NET 10, X64 RyuJIT). Mermaid bar-chart or table comparison.

### `docs/advanced.md` — `slug: /docs/advanced`, `sidebar_position: 10`

- `[SkipWhen(nameof(Condition))]` — conditional validation
- `[StopOnFirstFailure]` — short-circuit on first error
- `Severity` — `Error` vs `Warning` on a rule; how to filter by severity at the call site

## Tech Stack

- Plain Markdown (CommonMark)
- YAML frontmatter (`id`, `title`, `slug`, `description`, `sidebar_position`)
- Mermaid fenced blocks for diagrams
- No MDX, no React components
