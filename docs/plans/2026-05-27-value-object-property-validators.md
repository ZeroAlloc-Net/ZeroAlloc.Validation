# Value-Object-Aware Property Validators Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship ZeroAlloc.Validation 1.5.0 — built-in operand-taking validators (`[GreaterThan]`, `[NotEmpty]`, `[Matches]`, `[InclusiveBetween]`, etc.) accept `[ZeroAlloc.ValueObjects.ValueObject]`-typed properties directly by unwrapping through the single underlying member.

**Architecture:** One shared helper `BuildPropertyAccess(modelParamName, prop)` replaces three inline `$"{modelParamName}.{prop.Name}"` constructions in `RuleEmitter.cs`. The helper detects single-property value-objects via the `[ValueObject]` FQN attribute and returns the unwrap expression (`instance.Prop.Value`); otherwise the raw access. Multi-property value-objects fall through with a new `ZV0016` Warning. Predicate validators (`[Must]`, `[CustomValidation]`, `[ValidateWith]`) don't participate by design.

**Tech Stack:** .NET 10, Roslyn `IIncrementalGenerator`, xUnit, `CSharpGeneratorDriver` for snapshot tests.

**Design doc:** `docs/plans/2026-05-27-value-object-property-validators-design.md` (committed at `f86db6e`).

**Working branch:** `feat/value-object-aware-validators` (already created off `main`; design committed).

---

## Phase 0 — Orient (5 min)

Skim three locations before touching code.

### Task 0.1: Read the three rewrite sites

**Files (read-only):**

- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs:281-303` — `EmitPropertyRulesForProp` (lazy-allocation path). Builds `propAccess` at line 283, passes it to `BuildCondition`.
- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs:415-440` — `EmitFlatPathPropertyRules` (zero-failure-allocation path). Builds `propAccess` at line 417 the same way.
- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs:809-830` — `BuildPropertyValueExpr` (the `{value}` message-placeholder interpolator). Builds its own `access` at line 811.

All three currently use `$"{modelParamName}.{prop.Name}"`. The fix replaces each with a call to a new shared helper.

### Task 0.2: Read the existing `[ValueObject]` attribute + a sibling generator test

**Files (read-only):**

- `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.ValueObjects/src/ZeroAlloc.ValueObjects/ValueObjectAttribute.cs` — the marker. FQN: `ZeroAlloc.ValueObjects.ValueObjectAttribute`. ZA.Validation detects it by FQN match, no runtime reference.
- `tests/ZeroAlloc.Validation.Tests/Generator/StructValidationDiagnosticTests.cs` — same generator-snapshot test shape we want for the new diagnostic file.

---

## Phase 1 — Shared `BuildPropertyAccess` helper + integration TDD (45 min, 6 tasks)

### Task 1.1: Create the integration test file with a single failing TypedId case

**File (NEW):** `tests/ZeroAlloc.Validation.Tests/Integration/ValueObjectPropertyValidationTests.cs`

For value-object declarations in tests, **don't** add a ZA.ValueObjects package reference. The detection rule is FQN-only on the attribute name — so declaring an attribute with the matching FQN in test code is sufficient. Simplest approach: hand-roll a local `ZeroAlloc.ValueObjects.ValueObjectAttribute` in the test compilation. Existing tests in `Integration/` may already do this — read `StructValidationTests.cs` and surrounding files to see if there's an established pattern. If not, the file declares its own.

```csharp
#pragma warning disable MA0048 // multiple types intentionally co-located

using Xunit;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public class ValueObjectPropertyValidationTests
{
    [Fact]
    public void TypedId_GreaterThan_HappyPath_ReportsValid()
    {
        var validator = new PlaceOrderTypedCommandValidator();
        var result = validator.Validate(new PlaceOrderTypedCommand(new CustomerId(42)));
        Assert.True(result.IsValid);
        Assert.True(result.Failures.IsEmpty);
    }
}

// Local ValueObjectAttribute matching the ZA.ValueObjects FQN — no runtime ref needed.
namespace ZeroAlloc.ValueObjects
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}

namespace ZeroAlloc.Validation.Tests.Integration
{
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct CustomerId
    {
        public int Value { get; }
        public CustomerId(int value) => Value = value;
    }

    [Validate]
    public readonly record struct PlaceOrderTypedCommand(
        [property: GreaterThan(0)] CustomerId CustomerId);
}
```

Note: the `partial` keyword isn't strictly needed (no ZA.ValueObjects generator runs in tests; the struct is fully declared inline), but it matches the convention adopters will use. Keeping it makes the test feel realistic.

### Task 1.2: Run — expect FAIL (CS0019 or similar) via test-assembly build error

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet test tests/ZeroAlloc.Validation.Tests -c Release --filter "FullyQualifiedName~ValueObjectPropertyValidationTests"
```

Expected: **BUILD FAIL** with `CS0019: Operator '<=' cannot be applied to operands of type 'CustomerId' and 'int'` (or similar — the exact operator depends on which validator the generator picks for `[GreaterThan(0)]`).

This proves the bug exists in 1.4.1: the generator emits a primitive-typed comparison against the wrapper.

If it builds and the test passes, the generator is somehow already handling this — stop and report; the bug premise needs re-validation.

### Task 1.3: Implement the shared `BuildPropertyAccess` helper

**File:** `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

**Step 1:** Add the two helpers near the bottom of the file, alongside `NeedsNullGuard` (shipped in 1.4.1):

```csharp
    private const string ValueObjectAttributeFqn = "ZeroAlloc.ValueObjects.ValueObjectAttribute";

    /// <summary>
    /// If <paramref name="type"/> is a single-property value-object (decorated
    /// with <c>[ZeroAlloc.ValueObjects.ValueObject]</c> and declaring exactly one
    /// public instance property), returns that property's name. Returns null for
    /// everything else — class types, primitives, multi-property value-objects,
    /// or types without the marker attribute.
    /// </summary>
    private static string? GetValueObjectUnwrapMember(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return null;

        var hasMarker = named.GetAttributes()
            .Any(a => string.Equals(
                a.AttributeClass?.ToDisplayString(),
                ValueObjectAttributeFqn,
                StringComparison.Ordinal));
        if (!hasMarker) return null;

        var properties = named.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
            .ToArray();

        return properties.Length == 1 ? properties[0].Name : null;
    }

    /// <summary>
    /// True when <paramref name="type"/> carries <c>[ZeroAlloc.ValueObjects.ValueObject]</c>,
    /// regardless of property count. Used by the multi-property diagnostic (ZV0016)
    /// to detect "this is a value-object that auto-unwrap can't help with."
    /// </summary>
    private static bool HasValueObjectAttribute(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            ValueObjectAttributeFqn,
            StringComparison.Ordinal));

    /// <summary>
    /// Builds the access expression for a property's value. When the property's
    /// type is a single-property <c>[ValueObject]</c>, the expression unwraps
    /// through that property (e.g. <c>instance.CustomerId.Value</c>). Otherwise
    /// returns the raw access (<c>instance.CustomerId</c>).
    /// </summary>
    private static string BuildPropertyAccess(string modelParamName, IPropertySymbol prop)
    {
        var raw = $"{modelParamName}.{prop.Name}";
        var unwrapMember = GetValueObjectUnwrapMember(prop.Type);
        return unwrapMember is not null ? $"{raw}.{unwrapMember}" : raw;
    }
```

**Step 2:** Replace the three inline `$"{modelParamName}.{propName}"` constructions:

- `EmitPropertyRulesForProp` line 283: `var propAccess = BuildPropertyAccess(modelParamName, prop);` (drop `var propName = prop.Name;` if it's only used in the access — likely is, check the surrounding code; if it's used elsewhere keep it but read from `prop.Name` directly there)
- `EmitFlatPathPropertyRules` line 417: same replacement
- `BuildPropertyValueExpr` line 811: `var access = BuildPropertyAccess(modelParamName, prop);` (replaces the inline `$"{modelParamName}.{prop.Name}"`)

Verify nothing else inside those methods depends on the raw (un-unwrapped) access — the test from Task 1.1 + the regression net in Phase 2 are the safety net.

### Task 1.4: Run — expect the originally failing test now PASSES

```bash
dotnet test tests/ZeroAlloc.Validation.Tests -c Release --filter "FullyQualifiedName~ValueObjectPropertyValidationTests"
```

Expected: 1/1 pass.

If the test passes but a different existing test fails, the helper introduced a regression somewhere. Run the full suite (next step) and diagnose.

### Task 1.5: Run the FULL test suite — verify no regressions

```bash
dotnet test -c Release
```

Expected: every existing test passes. Primitive-typed and class-typed properties produce byte-identical output (helper returns the raw access for non-value-object types).

### Task 1.6: Commit

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs \
        tests/ZeroAlloc.Validation.Tests/Integration/ValueObjectPropertyValidationTests.cs
git commit -m "feat(generator): unwrap [ValueObject] properties in built-in validators

Adds GetValueObjectUnwrapMember + BuildPropertyAccess helpers and
routes the three propAccess construction sites in RuleEmitter
through them. When a property's declared type carries
[ZeroAlloc.ValueObjects.ValueObject] AND has exactly one public
instance property, the access expression unwraps through that
property (instance.CustomerId.Value instead of instance.CustomerId).
Every built-in operand-taking validator participates uniformly.

Predicate validators ([Must], [CustomValidation], [ValidateWith])
naturally don't participate — they take the property's identity,
not its operand-form access.

Multi-property value-objects + the ZV0016 diagnostic land in a
follow-up commit in the same release cycle."
```

---

## Phase 2 — Integration regression net (15 min, 3 tasks)

### Task 2.1: Add three more integration cases (sad path + string value-object + range)

**File:** `tests/ZeroAlloc.Validation.Tests/Integration/ValueObjectPropertyValidationTests.cs`

Inside the test class, after the existing `TypedId_GreaterThan_HappyPath_ReportsValid`, append:

```csharp
    [Fact]
    public void TypedId_GreaterThan_SadPath_ReportsFailureOnTypedIdProperty()
    {
        var validator = new PlaceOrderTypedCommandValidator();
        var result = validator.Validate(new PlaceOrderTypedCommand(new CustomerId(0)));
        Assert.False(result.IsValid);
        Assert.Equal(1, result.Failures.Length);
        Assert.Equal(nameof(PlaceOrderTypedCommand.CustomerId), result.Failures[0].PropertyName);
    }

    [Theory]
    [InlineData("alice", true)]
    [InlineData("", false)]
    public void StringValueObject_NotEmpty_Behaves(string raw, bool expectedValid)
    {
        var validator = new CreateUserCommandValidator();
        var result = validator.Validate(new CreateUserCommand(new Username(raw)));
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    public void TypedId_InclusiveBetween_Behaves(int raw, bool expectedValid)
    {
        var validator = new GetPageCommandValidator();
        var result = validator.Validate(new GetPageCommand(new PageNumber(raw)));
        Assert.Equal(expectedValid, result.IsValid);
    }
```

After the existing model declarations at the bottom of the file, append the new models inside the same `ZeroAlloc.Validation.Tests.Integration` namespace block (or alongside, depending on how the existing models are organised):

```csharp
[global::ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct Username
{
    public string Value { get; }
    public Username(string value) => Value = value;
}

[Validate]
public readonly record struct CreateUserCommand(
    [property: NotEmpty] Username Name);

[global::ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct PageNumber
{
    public int Value { get; }
    public PageNumber(int value) => Value = value;
}

[Validate]
public readonly record struct GetPageCommand(
    [property: InclusiveBetween(1, 100)] PageNumber Page);
```

### Task 2.2: Run — expect 4/4 pass

```bash
dotnet test -c Release --filter "FullyQualifiedName~ValueObjectPropertyValidationTests"
```

Expected: 4/4 pass — the sad path proves the `PropertyName` is still the wrapper-property's name (not `CustomerId.Value`); the string + range tests prove the rewrite applies uniformly across validator categories.

### Task 2.3: Commit

```bash
git add tests/ZeroAlloc.Validation.Tests/Integration/ValueObjectPropertyValidationTests.cs
git commit -m "test(generator): sad path + string + range coverage for value-object unwrap"
```

---

## Phase 3 — Generator-snapshot tests for emission shape (20 min, 3 tasks)

### Task 3.1: Create the diagnostic-snapshot test file

**File (NEW):** `tests/ZeroAlloc.Validation.Tests/Generator/ValueObjectPropertyDiagnosticTests.cs`

```csharp
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class ValueObjectPropertyDiagnosticTests
{
    [Fact]
    public void ValueObject_TypedId_Property_Rewrites_To_Unwrap_Member()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int value) => Value = value;
            }

            [Validate]
            public readonly record struct PlaceOrderCommand(
                [property: GreaterThan(0)] CustomerId CustomerId);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var validatorSource = GetGeneratedSource(result, "PlaceOrderCommandValidator.g.cs");
        Assert.Contains("instance.CustomerId.Value", validatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Primitive_Property_Still_Emits_Raw_Access()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public readonly record struct PlaceOrderCommand(
                [property: GreaterThan(0)] int CustomerId);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var validatorSource = GetGeneratedSource(result, "PlaceOrderCommandValidator.g.cs");
        Assert.Contains("instance.CustomerId", validatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("instance.CustomerId.Value", validatorSource, StringComparison.Ordinal);
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult result, string filenameSuffix) =>
        result.GeneratedTrees
            .First(t => t.FilePath.EndsWith(filenameSuffix, StringComparison.Ordinal))
            .ToString();

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        // Add a stub ValueObjectAttribute matching the ZA.ValueObjects FQN so the
        // generator's attribute-by-name lookup matches. No runtime reference to
        // ZA.ValueObjects needed.
        var valueObjectStub = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(valueObjectStub) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
```

If the sibling `StructValidationDiagnosticTests.cs` uses a slightly different `RunGenerator` shape (e.g. collection-syntax `[ ... ]` for the array literals or `string.Equals` for ID comparisons), match that convention.

### Task 3.2: Run — expect 2/2 pass

```bash
dotnet test -c Release --filter "FullyQualifiedName~ValueObjectPropertyDiagnosticTests"
```

Expected: 2/2 pass.

- Case 1 confirms the rewrite kicks in for value-object properties.
- Case 2 confirms the rewrite **doesn't** kick in for primitives — regression net.

If case 2 fails (the substring `instance.CustomerId.Value` appears in the primitive-typed generated source), the helper's null-return branch is broken. Stop and report.

### Task 3.3: Commit

```bash
git add tests/ZeroAlloc.Validation.Tests/Generator/ValueObjectPropertyDiagnosticTests.cs
git commit -m "test(generator): emission-shape snapshot for value-object unwrap

Cases:
- value-object TypedId property rewrites to instance.Prop.Value
- primitive property still emits raw instance.Prop (regression net)"
```

---

## Phase 4 — `ZV0016` Warning for multi-property value-objects (30 min, 5 tasks)

### Task 4.1: Add the failing diagnostic test (multi-property Money case)

**File:** `tests/ZeroAlloc.Validation.Tests/Generator/ValueObjectPropertyDiagnosticTests.cs`

Append after the existing two tests:

```csharp
    [Fact]
    public void MultiProperty_ValueObject_With_BuiltIn_Validator_Fires_ZV0016()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct Money
            {
                public decimal Amount { get; }
                public string Currency { get; }
                public Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = currency;
                }
            }

            [Validate]
            public readonly record struct PriceCommand(
                [property: GreaterThan(0)] Money Total);
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0016", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleProperty_ValueObject_Does_Not_Fire_ZV0016()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int value) => Value = value;
            }

            [Validate]
            public readonly record struct PlaceOrderCommand(
                [property: GreaterThan(0)] CustomerId CustomerId);
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0016", StringComparison.Ordinal));
    }
```

### Task 4.2: Run — expect 1 FAIL (`Fires_ZV0016`) + 1 PASS (`Does_Not_Fire`)

```bash
dotnet test -c Release --filter "FullyQualifiedName~ValueObjectPropertyDiagnosticTests"
```

Expected: 3/4 pass — the two Phase 3 cases stay green, `Does_Not_Fire_ZV0016` passes trivially (no diagnostic exists yet), `Fires_ZV0016` fails because the descriptor + emit aren't in place.

### Task 4.3: Add the `ZV0016` `DiagnosticDescriptor`

**File:** `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` (where the existing `ZV0011..ZV0015` descriptors live)

Insert after `ZV0015`:

```csharp
    private static readonly DiagnosticDescriptor ZV0016 = new DiagnosticDescriptor(
        id: "ZV0016",
        title: "Value-object with multiple properties can't be auto-unwrapped",
        messageFormat:
            "Property '{0}' of type '{1}' carries a built-in validator but '{1}' is a multi-property value-object ({2} properties). " +
            "Auto-unwrap requires exactly one underlying property. " +
            "Either declare the validator on a single-property value-object, or use [Must] / [CustomValidation] with a custom predicate.",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
```

### Task 4.4: Emit `ZV0016` from the rule-emission entry points

**File:** `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

The diagnostic fires when a property has at least one built-in validator rule AND its type carries `[ValueObject]` AND the unwrap helper returns `null` (i.e. multi-property or weird shape). The natural place is right at the rule-emission entry where `propAccess` is constructed.

`RuleEmitter` is called from `ValidatorGenerator.Emit` with a `SourceProductionContext ctx`. The current public entry points (`EmitPropertyRulesForProp`, `EmitFlatPathPropertyRules`) don't receive `ctx`. Two options:

1. **Thread `ctx` through** — change the method signatures to accept `SourceProductionContext ctx` and use `ctx.ReportDiagnostic`.
2. **Collect diagnostics into a List<Diagnostic>** that the caller reports.

Pick (1) — matches the pattern used in `EmitPropertyRulesForProp`'s caller chain. The methods need a `SourceProductionContext` parameter; pipe it from `Emit` down through `EmitFlatPath` / `EmitLazyPath` to the per-property emission methods.

Specifically, in `EmitPropertyRulesForProp` (lazy path):

```csharp
private static void EmitPropertyRulesForProp(
    SourceProductionContext ctx,   // ← new parameter
    StringBuilder sb,
    IPropertySymbol prop,
    List<AttributeData> rules,
    string modelParamName)
{
    var propAccess = BuildPropertyAccess(modelParamName, prop);

    // ZV0016: multi-property value-object can't be auto-unwrapped.
    if (rules.Count > 0
        && HasValueObjectAttribute(prop.Type)
        && GetValueObjectUnwrapMember(prop.Type) is null)
    {
        var propCount = ((INamedTypeSymbol)prop.Type).GetMembers()
            .OfType<IPropertySymbol>()
            .Count(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public);
        ctx.ReportDiagnostic(Diagnostic.Create(
            ZV0016,
            prop.Locations.FirstOrDefault() ?? Location.None,
            prop.Name, prop.Type.Name, propCount));
    }

    // …existing emission code continues unchanged…
}
```

Mirror the same diagnostic-fire-block in `EmitFlatPathPropertyRules`. Walk back up the call chain (`Emit` → `EmitFlatPath` / `EmitLazyPath` → these per-property methods) and add `SourceProductionContext ctx` to each signature.

`ZV0016` is declared in `ValidatorGenerator.cs` but the emission happens in `RuleEmitter.cs` — either expose it as `internal static readonly`, or move it to `RuleEmitter.cs`. The existing `ZV0011..ZV0015` are emitted from various places; check whether they're already `internal` and follow the same access modifier.

### Task 4.5: Update `AnalyzerReleases.Unshipped.md` + run + commit

**File:** `src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md`

Append (or extend the existing `### New Rules` table):

```markdown
### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|------------------------------
ZV0016  | ZeroAlloc.Validation | Warning  | Multi-property value-object can't be auto-unwrapped
```

Run:

```bash
dotnet test -c Release --filter "FullyQualifiedName~ValueObjectPropertyDiagnosticTests"
```

Expected: 4/4 pass.

```bash
dotnet test -c Release
```

Expected: full suite green.

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs \
        src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs \
        src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md \
        tests/ZeroAlloc.Validation.Tests/Generator/ValueObjectPropertyDiagnosticTests.cs
git commit -m "feat(generator): ZV0016 warning for multi-property value-objects

Auto-unwrap (the B1 ergonomics shipped above) requires the
[ValueObject] type to declare exactly one public property. Multi-
property value-objects like Money { Amount, Currency } can't be
unambiguously unwrapped; the generator falls through to its current
behaviour (which produces the existing CS0019 from comparing the
wrapper to a primitive) and additionally fires ZV0016 to tell the
adopter why.

Threads SourceProductionContext through the per-property emission
methods so diagnostics can be reported from the rule-emission entry
points."
```

---

## Phase 5 — `[Must]` predicate carve-out regression net (15 min, 3 tasks)

### Task 5.1: Add the `[Must]` regression test

**File:** `tests/ZeroAlloc.Validation.Tests/Generator/ValueObjectPropertyDiagnosticTests.cs`

Append:

```csharp
    [Fact]
    public void Must_Predicate_On_ValueObject_Property_Passes_Wrapper_Not_Unwrap()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int value) => Value = value;
            }

            public static class MustHelpers
            {
                public static bool IsKnown(CustomerId id) => id.Value > 0;
            }

            [Validate]
            public readonly record struct PlaceOrderCommand(
                [property: Must(typeof(MustHelpers), nameof(MustHelpers.IsKnown))] CustomerId CustomerId);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var validatorSource = GetGeneratedSource(result, "PlaceOrderCommandValidator.g.cs");
        // The Must predicate receives the wrapper, NOT instance.CustomerId.Value.
        Assert.Contains("instance.CustomerId", validatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("instance.CustomerId.Value", validatorSource, StringComparison.Ordinal);
    }
```

The exact `[Must]` attribute signature may differ from what's shown here — read `src/ZeroAlloc.Validation/Attributes/MustAttribute.cs` to confirm. Adjust the test source accordingly so it represents the canonical `[Must]` usage pattern.

### Task 5.2: Run — expect PASS (already covered by design, no code change)

```bash
dotnet test -c Release --filter "FullyQualifiedName~ValueObjectPropertyDiagnosticTests"
```

Expected: 5/5 pass — `[Must]` doesn't route through `propAccess`, so the rewrite never touches it.

If this test fails (i.e. the rewrite DID touch the `[Must]` emission), the implementation accidentally applies the rewrite somewhere that's not a built-in operand-taking validator. Investigate: trace where the `[Must]` validator emits its predicate call; if it uses `propAccess`, the carve-out needs an explicit check (skip `BuildPropertyAccess` rewrite when the attribute is `MustAttribute` / `CustomValidationAttribute` / `ValidateWithAttribute`).

If a fix is needed, the simplest shape is to filter at the `BuildPropertyAccess` call site:

```csharp
// Build raw access; predicate validators pass it through unchanged.
var rawAccess = $"{modelParamName}.{prop.Name}";
// Pass to emission, which decides whether to unwrap per-attribute (it shouldn't —
// but if the implementation conflates them, fix here).
```

Adjust and retry.

### Task 5.3: Commit

```bash
git add tests/ZeroAlloc.Validation.Tests/Generator/ValueObjectPropertyDiagnosticTests.cs
git commit -m "test(generator): [Must] predicate carve-out — receives wrapper, not unwrap"
```

---

## Phase 6 — Docs (15 min, 3 tasks)

### Task 6.1: Update `docs/getting-started.md`

**File:** `docs/getting-started.md`

Find the section introducing `[Validate]` (search for the literal `[Validate]` or the first code sample). Insert a new subsection — placement: after the basic `[Validate]` introduction, before the `[ValidateWith]` / nested-validators section.

```markdown
### Validating value-object properties

`[Validate]` request types can carry properties typed as
[`ZeroAlloc.ValueObjects`](https://www.nuget.org/packages/ZeroAlloc.ValueObjects)
value-objects, and the built-in validators unwrap to the value-object's underlying
property automatically:

```csharp
[ValueObject]
public readonly partial struct CustomerId
{
    public int Value { get; }
    public CustomerId(int value) => Value = value;
}

[Validate]
public readonly record struct PlaceOrderCommand(
    [property: GreaterThan(0)] CustomerId CustomerId);
```

`[GreaterThan(0)]` here compares against `CustomerId.Value` — the wrapper is
transparently unwrapped during validation. Same applies to `[InclusiveBetween]`,
`[NotEmpty]`, `[Matches]`, `[Length]`, and every other built-in operand-taking
validator.

**Single-property requirement.** Auto-unwrap works for value-objects that
declare exactly one public property (the typical "typed id" shape). Multi-
property value-objects like `Money { Amount, Currency }` aren't unwrapped —
the generator fires `ZV0016` and the build fails on the underlying type
mismatch. Use a `[Must]` or `[CustomValidation]` predicate to validate
multi-property value-objects against a custom rule.

**Predicate carve-out.** `[Must]`, `[CustomValidation]`, and `[ValidateWith]`
receive the value-object itself (the wrapper), not the unwrapped value. The
user-provided predicate signature dictates which form the value arrives in.
```

### Task 6.2: Add `ZV0016` to `docs/diagnostics.md`

**File:** `docs/diagnostics.md`

Add a row to the summary table (search for `ZV0014` / `ZV0015` to find the table) and a `## ZV0016` section between `ZV0015` and the end (or wherever the numeric order dictates). Style-match existing entries (colon-terminated bold labels per the B2 follow-up: `**Severity:**`, `**Title:**`, `**When fired:**`, `**Fix:**`).

```markdown
## ZV0016 — Multi-property value-object can't be auto-unwrapped

**Severity:** Warning

**Title:** Value-object with multiple properties can't be auto-unwrapped

**When fired:** A property's type is decorated with `[ZeroAlloc.ValueObjects.ValueObject]`,
the property carries at least one built-in operand-taking validator (e.g.
`[GreaterThan]`, `[NotEmpty]`, `[Matches]`), and the value-object declares
more than one public instance property. The generator can't pick a single
underlying member to unwrap to.

**Fix:** Pick one of:

- Use a single-property value-object (typical TypedId shape — one `Value`
  property wrapping a primitive).
- Replace the built-in validator with `[Must]` or `[CustomValidation]` and
  validate the wrapper directly.

**Example that fires the warning:**

```csharp
[ValueObject]
public readonly partial struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }
}

[Validate]
public readonly record struct PriceCommand(
    [property: GreaterThan(0)] Money Total);   // ZV0016
```

**Suppressing.** This warning typically accompanies a CS0019 (type mismatch)
that blocks the build anyway. If you have a legitimate reason to compile a
multi-property value-object through a built-in validator path (rare; usually
indicates a design issue), suppress with `#pragma warning disable ZV0016` or
`<NoWarn>$(NoWarn);ZV0016</NoWarn>`.
```

### Task 6.3: Commit

```bash
git add docs/getting-started.md docs/diagnostics.md
git commit -m "docs: ZV0016 + value-object validator unwrap section"
```

---

## Phase 7 — Backlog + ship (15 min, 4 tasks)

### Task 7.1: Run the full suite one final time

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Validation
dotnet test -c Release
```

Expected: every test passes — the 4 integration cases + the 5 generator-snapshot cases + every existing test.

### Task 7.2: Push + open the PR

```bash
git push -u origin feat/value-object-aware-validators
gh pr create \
  --title "feat(generator): unwrap [ValueObject] properties in built-in validators" \
  --body "$(cat <<'EOF'
## Summary

Closes backlog B1. Built-in operand-taking validators (``[GreaterThan]``, ``[NotEmpty]``, ``[InclusiveBetween]``, ``[Matches]``, ``[Length]``, …) now unwrap properties whose type is a single-property ``[ZeroAlloc.ValueObjects.ValueObject]``. The validator emits its comparison/regex/length code against ``instance.Prop.Value`` instead of ``instance.Prop``, so this becomes legal:

```csharp
[Validate]
public readonly record struct PlaceOrderCommand(
    [property: GreaterThan(0)] CustomerId CustomerId,
    [property: NotEmpty] Username Name);
```

Previously the only workaround was to drop value-objects from request types and re-wrap manually in the handler.

Surfaced 2026-05-26 building the ``za-vertical-slice`` template. The template's six request types currently carry raw ``int`` / ``decimal`` properties for exactly this reason; once 1.5.0 propagates, a follow-up template PR can adopt typed properties.

## What changed

- Two new private helpers in ``RuleEmitter.cs``:
  - ``GetValueObjectUnwrapMember(ITypeSymbol)`` — returns the underlying property name when the type is a single-property ``[ValueObject]``; ``null`` otherwise.
  - ``BuildPropertyAccess(modelParamName, prop)`` — central access-expression builder. Returns ``instance.Prop.Value`` for value-objects, ``instance.Prop`` otherwise.
- Three rewrite sites consolidated through ``BuildPropertyAccess``:
  - ``EmitPropertyRulesForProp`` (lazy-allocation path)
  - ``EmitFlatPathPropertyRules`` (zero-failure-allocation path)
  - ``BuildPropertyValueExpr`` (the ``{value}`` message-placeholder interpolator)
- New ``ZV0016`` Warning when a property carries a built-in validator and the type is a multi-property ``[ValueObject]`` (e.g. ``Money { Amount, Currency }``). Generator falls through to current behaviour; CS0019 still blocks the build but ZV0016 explains why auto-unwrap didn't help.
- 4 integration tests covering: TypedId happy + sad paths, string value-object ``[NotEmpty]`` ``[Theory]``, range validator on a TypedId.
- 5 generator-snapshot tests covering: value-object rewrite present, primitive rewrite absent (regression net), multi-property ZV0016 fires, single-property doesn't fire, ``[Must]`` predicate carve-out.

## Decisions ([design doc](docs/plans/2026-05-27-value-object-property-validators-design.md))

- **FQN-based attribute detection** — ZA.Validation never references ZA.ValueObjects at runtime; only matches ``ZeroAlloc.ValueObjects.ValueObjectAttribute`` by metadata name. Adopters who don't use ZA.ValueObjects pay nothing.
- **Single-property requirement.** Multi-property value-objects fall through with ``ZV0016`` — the user picks either a single-property shape or a custom predicate. Considered ``MemberOf`` hint on the validator attribute; YAGNI-deferred.
- **Predicate validator carve-out.** ``[Must]``, ``[CustomValidation]``, ``[ValidateWith]`` receive the wrapper, not the unwrap — their user-controlled signatures dictate the type. Falls out naturally from rewriting at ``propAccess`` (which predicate validators don't consume).

## SemVer

``1.4.1`` → ``1.5.0`` (additive minor — new shapes compile that previously didn't; existing class/primitive consumers see byte-identical generator output).

## Test plan

- [x] ``dotnet test -c Release`` — all green locally on net8/net9/net10 (existing ~940 + 9 new = ~950)
- [ ] CI — green on this PR
- [ ] Follow-up after 1.5.0 propagates: ``ZeroAlloc.Templates`` migrates ``za-vertical-slice``'s 6 request types from raw ``int`` / ``decimal`` properties to typed ``CustomerId`` / ``OrderId`` / etc., closing the original friction loop.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

### Task 7.3: Watch CI

```bash
gh pr checks --watch
```

Expected outcomes:

- ``build`` green
- ``aot-smoke`` green — the helper change has no AOT impact (all reflection happens at generator time, never at runtime)
- ``api-compat / api-compat`` green — strictly additive API surface

If anything is red, diagnose on-branch. Possible failure modes: snapshot drift in another generator test that wasn't aware the access-expression construction was consolidated; the ``[Must]`` carve-out test catching a latent rewrite where the implementation conflates predicate and operand validators.

### Task 7.4: After merge — mark B1 ✅ shipped in `docs/backlog.md`

After CI lands green:

```bash
gh pr merge --admin --squash --delete-branch
```

Release-please opens ``chore(main): release 1.5.0``. Admin-merge that too. Verify NuGet propagation (2-5 min):

```bash
curl -s "https://api.nuget.org/v3-flatcontainer/zeroalloc.validation/index.json" \
  | python -c "import sys, json; v = json.load(sys.stdin)['versions']; print('latest:', v[-1])"
```

Expected: ``latest: 1.5.0``.

**B1 backlog hygiene** — strike B1 in ``docs/backlog.md`` with the same pattern B2 / B3 used (strikethrough heading + ``— ✅ shipped 1.5.0 (2026-05-27)`` + prepend a Shipped block; original body kept in ``<details>``). Commit on ``main`` with:

```
docs(backlog): mark B1 shipped (1.5.0)
```

---

## Verification checklist

- [ ] **Phase 1:** Value-object TypedId compiles + validates; helper returns underlying property name; ``BuildPropertyAccess`` consolidates three sites.
- [ ] **Phase 2:** Sad path reports ``PropertyName`` as the wrapper-property name (not ``CustomerId.Value``); string + range validators participate in the rewrite uniformly.
- [ ] **Phase 3:** Generated ``.g.cs`` contains ``instance.Prop.Value`` for value-object properties; contains ``instance.Prop`` (no ``.Value``) for primitives — regression net.
- [ ] **Phase 4:** ``ZV0016`` Warning fires on multi-property value-objects, doesn't fire on single-property ones.
- [ ] **Phase 5:** ``[Must]`` predicate carve-out: generated ``.g.cs`` passes the wrapper to the predicate, NOT the unwrap.
- [ ] **Phase 6:** ``docs/getting-started.md`` callout + ``docs/diagnostics.md`` ZV0016 entry landed.
- [ ] **Phase 7:** CI green, admin-merged, release-please cuts 1.5.0, NuGet propagates, B1 marked shipped in backlog.

## Out of scope (deferred)

- **Multi-property value-object support** via a ``MemberOf`` hint or similar. ZV0016 documents the limit; if a real consumer needs it, separate brainstorm.
- **Collection-element value-objects** (``IReadOnlyList<MyValueObject>`` where elements carry ``[ValueObject]``). The B3 fix already covered the value-type collection-element case for ``[Validate]``-decorated elements; ``[ValueObject]``-only elements would need an analogous unwrap rule at the nested-validator emission sites. Separate enhancement.
- **Predicate-validator opt-in unwrap** (``[MustOnValue]`` or similar). Not in this PR; users currently opt in/out via their predicate signature.
- **Template migration follow-through** — ``za-vertical-slice`` request types stay on raw ``int`` / ``decimal`` until 1.5.0 propagates to NuGet, then a follow-up PR in ``ZeroAlloc.Templates`` adopts typed properties.
