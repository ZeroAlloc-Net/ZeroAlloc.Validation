# AGENTS.md: ZeroAlloc.Validation

A source-generated, attribute-based validation library for .NET, the ZeroAlloc alternative to
FluentValidation. A Roslyn generator emits a validator class for each `[Validate]` model. Five
NuGet packages ship: `ZeroAlloc.Validation` (runtime, attributes and the bundled generator),
`.Inject`, `.Options`, `.AspNetCore` and `.Testing`.

## Layout

- `src/*.Generator` projects target `netstandard2.0` and are not libraries consumers reference:
  the generator is packed into the runtime package. `ZeroAlloc.Validation.Options.Generator` and
  `ZeroAlloc.Validation.AspNetCore.Generator` are `IsPackable=false` and ship inside
  `.Options` and `.AspNetCore`. `.Inject` is a `netstandard2.0` generator package too.
- Generator helper sources are compiled into several generator projects rather than referenced
  across them, so a reference does not load a whole generator into another. Check every project
  that links a shared file before changing it.
- `tests/Directory.Build.props` imports the root one, so the root analyzers and
  `TreatWarningsAsErrors` apply to tests. `benchmarks/Directory.Build.props` does not import it:
  benchmarks build without `TreatWarningsAsErrors`, the root analyzers and the version guard.
- Public API is locked: `src/ZeroAlloc.Validation/PublicAPI.Shipped.txt` and
  `PublicAPI.Unshipped.txt`, with RS0016 and RS0017 as errors. A public change must update
  `PublicAPI.Unshipped.txt`. New generator diagnostics go in
  `src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md`. The release PR moves both
  files to Shipped; do not edit the Shipped files by hand.
- `tests/ZeroAlloc.Validation.PackSmoke` packs the shipped projects into a temporary feed and
  builds consumers against the `.nupkg` files. It is the only test that catches packaging
  defects, because every other test loads the generators by `ProjectReference`.

## Build and test

SDK: `global.json` pins 11.0.100 with `rollForward: latestMinor`. Test projects target
`net10.0;net11.0`, the same frameworks as the runtime libraries, so a full `dotnet test` needs
the .NET 10 and 11 runtimes. The AOT
sample targets `net11.0` and publishes with clang and the zlib headers installed. The benchmark
project targets `net10.0` only.

CI job `build` in `.github/workflows/ci.yml`:

```bash
dotnet tool restore
VERSION=$(dotnet gitversion /showvariable SemVer)
dotnet restore
dotnet build --no-restore -c Release -p:Version=$VERSION
dotnet test --no-build -c Release --verbosity normal --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
for csproj in src/*/*.csproj; do
  dotnet pack "$csproj" --no-build -c Release -p:Version=$VERSION -o ./artifacts
done
```

The coverage gate after the test step is the `ci-baseline` action `coverage-gate` on
`TestResults/**/coverage.cobertura.xml`; the commands above do not reproduce it. `codecov.yml`
must keep the project drop check. Delete `TestResults` and `artifacts` afterwards.

CI job `aot-smoke` (`ci.yml`):

```bash
dotnet restore samples/ZeroAlloc.Validation.AotSmoke/ZeroAlloc.Validation.AotSmoke.csproj
dotnet publish samples/ZeroAlloc.Validation.AotSmoke/ZeroAlloc.Validation.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out
timeout --kill-after=10s 120s ./aot-out/ZeroAlloc.Validation.AotSmoke
```

CI job `api-compat` (`ci.yml`) is the shared `ZeroAlloc-Net/.github` workflow, not a local command.

CI job `sdk10-consumer` (`ci.yml`) needs a 10.0.1xx SDK; `tests/ZeroAlloc.Validation.Sdk10Smoke/global.json`
pins 10.0.100 with `rollForward: latestPatch`, so a newer band cannot stand in for it. It builds and runs
a consumer of the four generator-bearing packages with Roslyn 5.0:

```bash
v=9.9.9-local.sha$(git rev-parse --short HEAD).$(date +%s)
for p in ZeroAlloc.Validation ZeroAlloc.Validation.Inject ZeroAlloc.Validation.Options ZeroAlloc.Validation.AspNetCore; do
  dotnet pack src/$p -c Release -o artifacts -p:Version="$v"
done
cd tests/ZeroAlloc.Validation.Sdk10Smoke/Consumer
dotnet run -c Release -p:ZeroAllocValidationVersion="$v"
```

CI job `roslyn-floor` (`ci.yml`) is the shared `ZeroAlloc-Net/.github` workflow: it packs the solution
and fails when a packed DLL references Roslyn above 5.0.0.

CI workflow `benchmarks.yml` runs the shared `benchmark-smoke.yml` gate on
`benchmarks/ZeroAlloc.Validation.Benchmarks`; its manual `full` job builds and runs:

```bash
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release
dotnet run --project benchmarks/ZeroAlloc.Validation.Benchmarks -c Release --no-build -- --filter "${FILTER:-*}"
```

Fast inner loop, verified locally. Every command that builds passes `-p:Version`, because the
guard in `Directory.Build.props` fails a pack without one:

```bash
SHA=$(git rev-parse --short HEAD)
dotnet test tests/ZeroAlloc.Validation.Tests -c Release -f net10.0 -p:Version=9.9.9-local.$SHA --filter "FullyQualifiedName~AllocationRegressionTests"
dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release --no-incremental
dotnet run --project benchmarks/ZeroAlloc.Validation.Benchmarks -c Release --no-build -- --filter "${FILTER:-*Flat*}" --job Dry --inProcess
```

Benchmarks need `--no-incremental`: the generator output assembly is not an MSBuild incremental
input (`ReferenceOutputAssembly="false"`), so a generator change is otherwise not picked up.
`--job Dry` is a smoke run; its numbers mean nothing. Delete `BenchmarkDotNet.Artifacts` afterwards.

## Conventions and gotchas

- Every generator project references `Microsoft.CodeAnalysis.CSharp` 5.0.0, the compiler of SDK band
  10.0.1xx, the oldest supported SDK, and the bundled `ZeroAlloc.Pipeline.Generators` is 2.0.1 or later,
  which does too. A compiler older than the Roslyn a bundled DLL references skips the generator with
  `CS9057`, and nothing is generated. The shared Renovate preset holds it below 5.1. If a newer Roslyn
  API is needed, change the code; raise the floor only together with the minimum SDK. Test projects may
  use a newer Roslyn.
- Every pack needs the version stamped into the assembly. Build and pack with the same
  `-p:Version`; `-p:PackageVersion` alone fails with ZAVERSION002. Pack with `--no-build` only
  after a build that got `-p:Version`.
- The valid path of `Validate` allocates 0 B, asserted by `AllocationRegressionTests` for flat,
  nested, collection, generic, enum, precision and custom-rule models, and for the synchronously
  completing `ValidateAsync` path. A failing validation allocates exactly one result array, 56 B
  for one failure on 64-bit runtimes (24 B header plus a 32 B `ValidationFailure`), asserted
  exactly. Nested failures add a further asserted amount. Keep those budgets exact, never loosen
  them to a range.
- Test the generated validators through `tests/ZeroAlloc.Validation.Tests/Integration` and the
  emitted source through `tests/ZeroAlloc.Validation.Tests/Generator`; a hand-written validator
  proves nothing about the generator.
- `ZeroAlloc.Validation.Tests.MSTest` and `.NUnit` exist to prove `.Testing` works with those
  frameworks; the rest of the tests use xunit.
- Exception to the org rule that benchmarks target the library's LTS frameworks: the benchmark
  project targets only `net10.0`. The AOT smoke sample targets only `net11.0`, the SDK global.json
  pins, because `PublishAot` runs there.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
