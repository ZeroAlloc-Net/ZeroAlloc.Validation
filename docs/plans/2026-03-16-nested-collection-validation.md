# Nested & Collection Validation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Complete nested/collection validation with constructor injection, `[ValidateWith]` escape hatch, and compile-time diagnostics ZV0011/ZV0012.

**Architecture:** Auto-compose already works using `new ValidatorName()` inline. This plan replaces that with constructor-injected fields (Task 1), adds `[ValidateWith(typeof(T))]` for third-party types (Task 2), and adds two Roslyn diagnostics (Task 3).

**Tech Stack:** Roslyn `IIncrementalGenerator` (netstandard2.0), `SourceProductionContext.ReportDiagnostic`, xUnit, ZValidation attribute-based design.

---

## Context

The generator (`src/ZValidation.Generator/RuleEmitter.cs`) already:
- Detects properties whose type has `[Validate]` → auto-composes nested validation
- Detects collection properties whose element type has `[Validate]` → collection validation
- Emits dot-notation (`ShippingAddress.Street`) and bracket-notation (`Items[0].Sku`) paths
- Has full integration tests (`NestedValidationTests`, `CollectionValidationTests`) and generator unit tests

**What's missing per the approved design:**
1. Constructor injection — currently emits `new AddressValidator().Validate(...)`, needs `_addressValidator.Validate(...)`
2. `[ValidateWith(typeof(T))]` attribute — for types without `[Validate]` (third-party)
3. ZV0011 warning — `[ValidateWith]` on a property whose type already has `[Validate]` is redundant
4. ZV0012 error — `[ValidateWith(typeof(T))]` where `T` doesn't implement `ValidatorFor<TProperty>`

---

## Task 1: Constructor Injection

**Files:**
- Modify: `src/ZValidation.Generator/RuleEmitter.cs`
- Modify: `src/ZValidation.Generator/ValidatorGenerator.cs`
- Modify: `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`
- Modify: `tests/ZValidation.Tests/Integration/NestedValidationTests.cs`
- Modify: `tests/ZValidation.Tests/Integration/CollectionValidationTests.cs`

### Step 1: Write four failing generator tests

Add these tests to `GeneratorRuleEmissionTests.cs` before the `RunGeneratorGetSource` method:

```csharp
[Fact]
public void Generator_EmitsConstructorParam_ForNestedValidateType()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
        [Validate] public class Customer { public Address Home { get; set; } = new(); }
        """;

    var customerSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("CustomerValidator", StringComparison.Ordinal));

    Assert.Contains("AddressValidator homeValidator", customerSource, StringComparison.Ordinal);
    Assert.Contains("_homeValidator", customerSource, StringComparison.Ordinal);
}

[Fact]
public void Generator_EmitsConstructorParam_ForCollectionOfValidateType()
{
    var source = """
        using ZValidation;
        using System.Collections.Generic;
        namespace TestModels;
        [Validate] public class Item { [NotEmpty] public string Name { get; set; } = ""; }
        [Validate] public class Bag { public List<Item> Things { get; set; } = new(); }
        """;

    var bagSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("BagValidator", StringComparison.Ordinal));

    Assert.Contains("ItemValidator thingsValidator", bagSource, StringComparison.Ordinal);
    Assert.Contains("_thingsValidator", bagSource, StringComparison.Ordinal);
}

[Fact]
public void Generator_NoConstructor_WhenNoNestedProperties()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate] public class Plain { [NotEmpty] public string Name { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);

    Assert.DoesNotContain("public PlainValidator(", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_TwoNestedProperties_SameType_TwoDistinctParams()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
        [Validate] public class Order
        {
            public Address Shipping { get; set; } = new();
            public Address Billing  { get; set; } = new();
        }
        """;

    var orderSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("OrderValidator", StringComparison.Ordinal));

    Assert.Contains("_shippingValidator", orderSource, StringComparison.Ordinal);
    Assert.Contains("_billingValidator", orderSource, StringComparison.Ordinal);
}
```

### Step 2: Run tests to confirm they fail

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "EmitsConstructorParam|NoConstructor|TwoNestedProperties" -v minimal
```

Expected: 4 tests fail (no constructor is emitted yet).

### Step 3: Add `CollectNestedValidatorFields` to `RuleEmitter.cs`

Add a new public static method at the bottom of `RuleEmitter` (before the closing `}`):

```csharp
private const string ValidateWithAttributeFqn = "ZValidation.ValidateWithAttribute";

/// <summary>
/// Returns field info for every property that requires a nested validator via constructor injection.
/// Each entry: (FieldName, ParamName, QualifiedValidatorType)
/// e.g. ("_homeValidator", "homeValidator", "global::TestModels.AddressValidator")
/// </summary>
public static System.Collections.Generic.List<(string FieldName, string ParamName, string QualifiedValidatorType)>
    CollectNestedValidatorFields(INamedTypeSymbol classSymbol)
{
    var result = new System.Collections.Generic.List<(string, string, string)>();
    foreach (var member in classSymbol.GetMembers())
    {
        if (member is not IPropertySymbol prop) continue;

        // Skip properties that have [ValidateWith] — handled separately in Task 2
        // (but still collect them here as validator fields)
        string? qualifiedType = null;

        // Single nested type with [Validate]
        if (prop.Type is INamedTypeSymbol nestedNamed && HasValidateAttribute(nestedNamed))
        {
            var ns = nestedNamed.ContainingNamespace?.ToDisplayString();
            qualifiedType = IsGlobalOrEmpty(ns)
                ? $"global::{nestedNamed.Name}Validator"
                : $"global::{ns}.{nestedNamed.Name}Validator";
        }
        // Collection element type with [Validate]
        else if (GetCollectionElementType(prop) is INamedTypeSymbol elemNamed && HasValidateAttribute(elemNamed))
        {
            var ns = elemNamed.ContainingNamespace?.ToDisplayString();
            qualifiedType = IsGlobalOrEmpty(ns)
                ? $"global::{elemNamed.Name}Validator"
                : $"global::{ns}.{elemNamed.Name}Validator";
        }

        if (qualifiedType is null) continue;

        var propName = prop.Name;
        var camel = char.ToLowerInvariant(propName[0]).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + propName.Substring(1);
        result.Add(($"_{camel}Validator", $"{camel}Validator", qualifiedType));
    }
    return result;
}
```

### Step 4: Update `EmitNestedValidators` and `EmitCollectionValidators` in `RuleEmitter.cs`

Replace the `new ...().Validate(...)` calls with field references.

In `EmitNestedValidators`, change line:
```csharp
sb.AppendLine($"            var nestedResult = new {qualifiedValidatorName}().Validate({modelParamName}.{propName});");
```
To:
```csharp
var camel = char.ToLowerInvariant(propName[0]).ToString(System.Globalization.CultureInfo.InvariantCulture) + propName.Substring(1);
var fieldRef = $"_{camel}Validator";
sb.AppendLine($"            var nestedResult = {fieldRef}.Validate({modelParamName}.{propName});");
```

In `EmitCollectionValidators`, change:
```csharp
sb.AppendLine($"                    var {varName}Result = new {qualifiedCollValidatorName}().Validate({varName}Item);");
```
To:
```csharp
var camelCollProp = char.ToLowerInvariant(propName[0]).ToString(System.Globalization.CultureInfo.InvariantCulture) + propName.Substring(1);
var collFieldRef = $"_{camelCollProp}Validator";
sb.AppendLine($"                    var {varName}Result = {collFieldRef}.Validate({varName}Item);");
```

### Step 5: Update `ValidatorGenerator.cs` to emit fields and constructor

In the `Emit` method of `ValidatorGenerator.cs`, after the lifetime attribute emit (line ~60) and before the class opening, emit the fields and constructor. Insert the following after `sb.AppendLine($"public sealed partial class {validatorName} : ValidatorFor<{modelName}>");` and `sb.AppendLine("{");`:

```csharp
var nestedFields = RuleEmitter.CollectNestedValidatorFields(classSymbol);

if (nestedFields.Count > 0)
{
    foreach (var (fieldName, _, qualifiedType) in nestedFields)
        sb.AppendLine($"    private readonly {qualifiedType} {fieldName};");

    sb.AppendLine();

    // Constructor
    sb.Append($"    public {validatorName}(");
    for (int fi = 0; fi < nestedFields.Count; fi++)
    {
        var (fieldName, paramName, qualifiedType) = nestedFields[fi];
        if (fi > 0) sb.Append(", ");
        sb.Append($"{qualifiedType} {paramName}");
    }
    sb.AppendLine(")");
    sb.AppendLine("    {");
    foreach (var (fieldName, paramName, _) in nestedFields)
        sb.AppendLine($"        {fieldName} = {paramName};");
    sb.AppendLine("    }");
    sb.AppendLine();
}
```

### Step 6: Run the 4 new generator tests to confirm they pass

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "EmitsConstructorParam|NoConstructor|TwoNestedProperties" -v minimal
```

Expected: all 4 pass.

### Step 7: Update existing generator tests

The tests `Generator_EmitsNestedValidation_WithDotPrefix` and `Generator_EmitsCollectionValidation_WithBracketIndex` assert on `AddressValidator` / `LineItemValidator` appearing in the output — they will still pass because field declarations still reference those types. But the tests that assert `new ... ()` is absent need updating. Run the full generator suite to see which fail:

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "GeneratorRuleEmissionTests" -v minimal
```

Fix any failing tests (likely none — the existing tests assert presence of type names, not on `new()`).

### Step 8: Update integration test constructors

`tests/ZValidation.Tests/Integration/NestedValidationTests.cs` — change:
```csharp
private readonly OrderValidator _validator = new();
```
To:
```csharp
private readonly OrderValidator _validator = new(new AddressValidator(), new AddressValidator());
```

(`Order` has `ShippingAddress` and `BillingAddress`, both `Address` type → two `AddressValidator` params.)

`tests/ZValidation.Tests/Integration/CollectionValidationTests.cs` — change:
```csharp
private readonly CartValidator _validator = new();
```
To:
```csharp
private readonly CartValidator _validator = new(new LineItemValidator());
```

(`Cart` has `Items` of type `IList<LineItem>` → one `LineItemValidator` param.)

### Step 9: Run the full test suite

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```

Expected: all tests pass (should be 155 — 151 existing + 4 new).

### Step 10: Commit

```bash
git add src/ZValidation.Generator/RuleEmitter.cs
git add src/ZValidation.Generator/ValidatorGenerator.cs
git add tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs
git add tests/ZValidation.Tests/Integration/NestedValidationTests.cs
git add tests/ZValidation.Tests/Integration/CollectionValidationTests.cs
git commit -m "feat: use constructor injection for nested validator fields"
```

---

## Task 2: `[ValidateWith]` Attribute

**Files:**
- Create: `src/ZValidation/Attributes/ValidateWithAttribute.cs`
- Modify: `src/ZValidation.Generator/RuleEmitter.cs`
- Modify: `src/ZValidation.Generator/ValidatorGenerator.cs`
- Test: `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`
- Test: `tests/ZValidation.Tests/Integration/` (new model + test file)

### Step 1: Create the attribute

Create `src/ZValidation/Attributes/ValidateWithAttribute.cs`:

```csharp
namespace ZValidation;

/// <summary>
/// Specifies an explicit validator type for a property whose type does not carry [Validate].
/// Use for third-party or framework types you do not control.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
public sealed class ValidateWithAttribute : System.Attribute
{
    public ValidateWithAttribute(System.Type validatorType) => ValidatorType = validatorType;
    public System.Type ValidatorType { get; }
}
```

### Step 2: Write failing tests

Add to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_ValidateWith_UsesSpecifiedValidatorType()
{
    var source = """
        using ZValidation;
        namespace ThirdParty { public class Money { public decimal Amount { get; set; } } }
        namespace TestModels;
        [Validate] public class MoneyValidator : ValidatorFor<ThirdParty.Money>
        {
            public override global::ZValidation.ValidationResult Validate(ThirdParty.Money instance) =>
                new(new global::ZValidation.ValidationFailure[0]);
        }
        [Validate] public class Invoice
        {
            [ValidateWith(typeof(MoneyValidator))]
            public ThirdParty.Money Total { get; set; } = new();
        }
        """;

    var invoiceSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("InvoiceValidator", StringComparison.Ordinal));

    Assert.Contains("MoneyValidator", invoiceSource, StringComparison.Ordinal);
    Assert.Contains("_totalValidator", invoiceSource, StringComparison.Ordinal);
}

[Fact]
public void Generator_ValidateWith_Collection_UsesSpecifiedValidatorType()
{
    var source = """
        using ZValidation;
        using System.Collections.Generic;
        namespace ThirdParty { public class Tag { public string Name { get; set; } = ""; } }
        namespace TestModels;
        [Validate] public class TagValidator : ValidatorFor<ThirdParty.Tag>
        {
            public override global::ZValidation.ValidationResult Validate(ThirdParty.Tag instance) =>
                new(new global::ZValidation.ValidationFailure[0]);
        }
        [Validate] public class Article
        {
            [ValidateWith(typeof(TagValidator))]
            public List<ThirdParty.Tag> Tags { get; set; } = new();
        }
        """;

    var articleSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("ArticleValidator", StringComparison.Ordinal));

    Assert.Contains("TagValidator", articleSource, StringComparison.Ordinal);
    Assert.Contains("_tagsValidator", articleSource, StringComparison.Ordinal);
}
```

Run to confirm they fail:
```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "ValidateWith" -v minimal
```

### Step 3: Update `CollectNestedValidatorFields` in `RuleEmitter.cs`

Add a check for `[ValidateWith]` before the auto-detect logic. When a property has `[ValidateWith(typeof(T))]`, use `T` as the validator type regardless of whether the property type has `[Validate]`:

```csharp
// Check for [ValidateWith(typeof(T))] first
var validateWithAttr = prop.GetAttributes()
    .FirstOrDefault(a => string.Equals(
        a.AttributeClass?.ToDisplayString(), ValidateWithAttributeFqn, StringComparison.Ordinal));

if (validateWithAttr is not null)
{
    var specifiedType = validateWithAttr.ConstructorArguments.Length > 0
        ? validateWithAttr.ConstructorArguments[0].Value as INamedTypeSymbol
        : null;
    if (specifiedType is not null)
    {
        var ns2 = specifiedType.ContainingNamespace?.ToDisplayString();
        qualifiedType = IsGlobalOrEmpty(ns2)
            ? $"global::{specifiedType.Name}"
            : $"global::{ns2}.{specifiedType.Name}";
    }
}
```

Place this block in `CollectNestedValidatorFields`, replacing the `qualifiedType` assignment block. The `[ValidateWith]` check comes FIRST; the auto-detect for `[Validate]` is the fallback.

Also update `GetNestedValidateProperties` and `GetCollectionValidateProperties` in `RuleEmitter.cs` to also include properties with `[ValidateWith]` (so `EmitNestedPath` is triggered even for types that don't carry `[Validate]`).

Add a helper:
```csharp
private static bool HasValidateWithAttribute(IPropertySymbol prop) =>
    prop.GetAttributes().Any(a => string.Equals(
        a.AttributeClass?.ToDisplayString(), ValidateWithAttributeFqn, StringComparison.Ordinal));
```

Update `GetNestedValidateProperties`:
```csharp
private static IEnumerable<IPropertySymbol> GetNestedValidateProperties(INamedTypeSymbol classSymbol) =>
    classSymbol.GetMembers()
        .OfType<IPropertySymbol>()
        .Where(p => HasValidateWithAttribute(p)
            ? GetCollectionElementType(p) is null  // [ValidateWith] on non-collection
            : p.Type is INamedTypeSymbol t && HasValidateAttribute(t));
```

Update `GetCollectionValidateProperties`:
```csharp
private static IEnumerable<(IPropertySymbol Property, INamedTypeSymbol ElementType)>
    GetCollectionValidateProperties(INamedTypeSymbol classSymbol) =>
    classSymbol.GetMembers()
        .OfType<IPropertySymbol>()
        .Select(p =>
        {
            if (HasValidateWithAttribute(p) && GetCollectionElementType(p) is INamedTypeSymbol elem)
                return ((IPropertySymbol Property, INamedTypeSymbol? ElementType)?)(p, elem);
            if (!HasValidateWithAttribute(p))
            {
                var elem2 = GetCollectionElementType(p) as INamedTypeSymbol;
                if (elem2 is not null && HasValidateAttribute(elem2))
                    return (p, elem2);
            }
            return null;
        })
        .Where(x => x.HasValue)
        .Select(x => (x!.Value.Property, x.Value.ElementType!));
```

### Step 4: Run tests

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "ValidateWith" -v minimal
```

Expected: both new tests pass.

### Step 5: Run full suite

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```

Expected: all tests pass.

### Step 6: Commit

```bash
git add src/ZValidation/Attributes/ValidateWithAttribute.cs
git add src/ZValidation.Generator/RuleEmitter.cs
git commit -m "feat: add [ValidateWith] attribute for explicit nested validator override"
```

---

## Task 3: Analyzers ZV0011 and ZV0012

**Files:**
- Modify: `src/ZValidation.Generator/ValidatorGenerator.cs`
- Test: `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`

### Overview

Both diagnostics are reported from `ValidatorGenerator.Emit` via `ctx.ReportDiagnostic`.

- **ZV0011** (Warning): property has `[ValidateWith]` AND its type has `[Validate]` — the attribute is redundant
- **ZV0012** (Error): `[ValidateWith(typeof(T))]` where `T`'s generic argument doesn't match the property type

### Step 1: Write failing tests

Add to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Analyzer_ZV0011_Fires_WhenValidateWithOnAlreadyValidatedType()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
        [Validate] public class Customer
        {
            [ValidateWith(typeof(AddressValidator))]
            public Address Home { get; set; } = new();
        }
        """;

    var diagnostics = RunGeneratorGetDiagnostics(source);
    Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZV0011", StringComparison.Ordinal));
}

[Fact]
public void Analyzer_ZV0012_Fires_WhenValidateWithTypeDoesNotMatchProperty()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
        [Validate] public class Name    { [NotEmpty] public string Value  { get; set; } = ""; }
        [Validate] public class Customer
        {
            [ValidateWith(typeof(NameValidator))]
            public Address Home { get; set; } = new();
        }
        """;

    var diagnostics = RunGeneratorGetDiagnostics(source);
    Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZV0012", StringComparison.Ordinal));
}
```

Also add the `RunGeneratorGetDiagnostics` helper to the test class (alongside `RunGeneratorGetSource`):

```csharp
private static System.Collections.Generic.IReadOnlyList<Diagnostic> RunGeneratorGetDiagnostics(string source)
{
    var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
    var compilation = CSharpCompilation.Create(
        "TestAssembly",
        [CSharpSyntaxTree.ParseText(source)],
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
        ],
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    var generator = new ValidatorGenerator();
    var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
    return driver.GetRunResult().Diagnostics;
}
```

Run to confirm they fail:
```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "Analyzer_ZV" -v minimal
```

### Step 2: Add diagnostic descriptors to `ValidatorGenerator.cs`

Add two static fields at the top of `ValidatorGenerator`:

```csharp
private static readonly DiagnosticDescriptor ZV0011 = new(
    id: "ZV0011",
    title: "Redundant [ValidateWith] attribute",
    messageFormat: "Property '{0}' has [ValidateWith] but its type '{1}' already has [Validate]. Remove [ValidateWith] to use the auto-generated validator.",
    category: "ZValidation",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);

private static readonly DiagnosticDescriptor ZV0012 = new(
    id: "ZV0012",
    title: "Invalid [ValidateWith] validator type",
    messageFormat: "Validator type '{0}' specified via [ValidateWith] on property '{1}' does not implement ValidatorFor<{2}>.",
    category: "ZValidation",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

### Step 3: Emit diagnostics in the `Emit` method

In `ValidatorGenerator.Emit`, before calling `RuleEmitter.EmitValidateBody`, iterate over properties and check conditions:

```csharp
private const string ValidateWithFqn = "ZValidation.ValidateWithAttribute";
private const string ValidatorForFqn = "ZValidation.ValidatorFor<T>";

// Check each property for ZV0011 and ZV0012
foreach (var member in classSymbol.GetMembers())
{
    if (member is not IPropertySymbol prop) continue;

    var validateWithAttr = prop.GetAttributes()
        .FirstOrDefault(a => string.Equals(
            a.AttributeClass?.ToDisplayString(), ValidateWithFqn, StringComparison.Ordinal));

    if (validateWithAttr is null) continue;

    var propTypeName = prop.Type.Name;

    // ZV0011: property type already has [Validate]
    if (prop.Type is INamedTypeSymbol propNamed
        && propNamed.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, StringComparison.Ordinal)))
    {
        ctx.ReportDiagnostic(Diagnostic.Create(ZV0011,
            validateWithAttr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? member.Locations.FirstOrDefault(),
            prop.Name, propTypeName));
    }

    // ZV0012: specified validator type doesn't implement ValidatorFor<TProp>
    var specifiedType = validateWithAttr.ConstructorArguments.Length > 0
        ? validateWithAttr.ConstructorArguments[0].Value as INamedTypeSymbol
        : null;

    if (specifiedType is not null)
    {
        // Get the property's non-collection type (element type for collections, else property type)
        ITypeSymbol expectedModelType = prop.Type;
        if (RuleEmitter.GetCollectionElementTypePublic(prop) is { } elemType)
            expectedModelType = elemType;

        // Check if specifiedType implements ValidatorFor<expectedModelType>
        bool implementsValidatorFor = specifiedType.AllInterfaces.Any(iface =>
            iface.IsGenericType
            && string.Equals(iface.OriginalDefinition.ToDisplayString(),
                "ZValidation.ValidatorFor<T>", StringComparison.Ordinal)
            && iface.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], expectedModelType))
            || (specifiedType.BaseType is { IsGenericType: true } baseType
            && string.Equals(baseType.OriginalDefinition.ToDisplayString(),
                "ZValidation.ValidatorFor<T>", StringComparison.Ordinal)
            && baseType.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(baseType.TypeArguments[0], expectedModelType));

        if (!implementsValidatorFor)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(ZV0012,
                validateWithAttr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? member.Locations.FirstOrDefault(),
                specifiedType.Name, prop.Name, expectedModelType.Name));
        }
    }
}
```

Also expose `GetCollectionElementType` as internal in `RuleEmitter.cs`:
```csharp
internal static ITypeSymbol? GetCollectionElementTypePublic(IPropertySymbol prop) => GetCollectionElementType(prop);
```

### Step 4: Run the diagnostic tests

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "Analyzer_ZV" -v minimal
```

Expected: both pass.

### Step 5: Run full suite

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```

Expected: all tests pass.

### Step 6: Commit

```bash
git add src/ZValidation.Generator/ValidatorGenerator.cs
git add src/ZValidation.Generator/RuleEmitter.cs
git add tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add ZV0011/ZV0012 diagnostics for [ValidateWith] attribute validation"
```

---

## Notes

- All generator code is `netstandard2.0` — use two-arg `string.Replace`, no `StringComparison` overload on `Replace`.
- `TreatWarningsAsErrors=true` — no warnings allowed in any project.
- `EnforceExtendedAnalyzerRules=true` — no `System.IO`, no `System.Threading` in generator code.
- `ValidatorFor<T>` FQN check: both `AllInterfaces` and `BaseType` must be checked since the generated validators extend `ValidatorFor<T>` directly (class, not interface).
