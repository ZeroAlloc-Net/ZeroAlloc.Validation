# Collection Validation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the source generator automatically validate each element of a collection property whose element type is decorated with `[Validate]`, using bracket-indexed dot-prefixed property names (`Items[0].Sku`).

**Architecture:** Extend `RuleEmitter` with three new helpers that detect collection properties (`GetCollectionElementType`, `HasCollectionValidateProperties`, `GetCollectionValidateProperties`). Wire these into `EmitValidateBody` alongside the existing nested-object detection: when a model has either nested objects or collection properties, the List-based branch is taken and foreach loops are emitted per collection. Flat models are unaffected.

**Tech Stack:** C# 13, Roslyn `IIncrementalGenerator`, `INamedTypeSymbol`, `IArrayTypeSymbol`, `Microsoft.CodeAnalysis.CSharp` 4.11.0, xUnit 2.9.3.

---

## Reference

Design doc: `docs/plans/2026-03-15-collection-validation-design.md`

Key existing files:
- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` — **modify this** (`EmitValidateBody` + new helpers)
- `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` — no changes needed
- `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs` — add generator unit tests
- `tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs` — add integration tests

---

### Task 1: Detect collection properties with `[Validate]` element types

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Update `RunGeneratorGetSources` to include `System.Collections.dll`**

The generator tests compile user source that uses `List<T>`. Roslyn needs the `System.Collections` assembly to resolve it. Update **both** helpers in `GeneratorRuleEmissionTests.cs` to add this reference:

```csharp
// Add this line to the metadata references array in BOTH RunGeneratorGetSource and RunGeneratorGetSources:
MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
```

So both helpers end up with 4 references:
```csharp
[
    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
    MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
    MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
    MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
]
```

Note: `RunGeneratorGetSources` does not store `systemRuntime` in a local variable — inline the call:
```csharp
MetadataReference.CreateFromFile(System.IO.Path.Combine(
    System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll")),
```

**Step 2: Write the failing test**

Add to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_UsesListForModelWithCollectionOfValidateType()
{
    var source = """
        using ZeroAlloc.Validation;
        using System.Collections.Generic;
        namespace TestModels;

        [Validate]
        public class LineItem
        {
            [NotEmpty]
            public string Sku { get; set; } = "";
        }

        [Validate]
        public class Order
        {
            [NotEmpty]
            public string Reference { get; set; } = "";
            public List<LineItem> LineItems { get; set; } = new();
        }
        """;

    var orderSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("OrderValidator"));

    Assert.Contains("List<", orderSource);
    Assert.DoesNotContain("List<", RunGeneratorGetSources(source)
        .First(s => s.Contains("LineItemValidator")));
}
```

**Step 3: Run to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_UsesListForModelWithCollectionOfValidateType"
```

Expected: FAIL — `Order` currently uses fixed array since no nested `[Validate]` object properties exist.

**Step 4: Add the three collection detection helpers to `RuleEmitter.cs`**

Add these after the existing `HasValidateAttribute` helper at the bottom of the file:

```csharp
private static ITypeSymbol? GetCollectionElementType(IPropertySymbol prop)
{
    // T[]
    if (prop.Type is IArrayTypeSymbol arr)
        return arr.ElementType;

    if (prop.Type is not INamedTypeSymbol named)
        return null;

    // IEnumerable<T> directly
    if (named.IsGenericType && named.TypeArguments.Length == 1
        && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
        return named.TypeArguments[0];

    // Any type implementing IEnumerable<T> (List<T>, IList<T>, ICollection<T>, etc.)
    foreach (var iface in named.AllInterfaces)
    {
        if (iface.IsGenericType && iface.TypeArguments.Length == 1
            && iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
            return iface.TypeArguments[0];
    }

    return null;
}

private static bool HasCollectionValidateProperties(INamedTypeSymbol classSymbol) =>
    classSymbol.GetMembers()
        .OfType<IPropertySymbol>()
        .Any(p => GetCollectionElementType(p) is INamedTypeSymbol t && HasValidateAttribute(t));

private static IEnumerable<(IPropertySymbol Property, INamedTypeSymbol ElementType)> GetCollectionValidateProperties(INamedTypeSymbol classSymbol) =>
    classSymbol.GetMembers()
        .OfType<IPropertySymbol>()
        .Select(p => (Property: p, ElementType: GetCollectionElementType(p) as INamedTypeSymbol))
        .Where(x => x.ElementType is not null && HasValidateAttribute(x.ElementType!))
        .Select(x => (x.Property, x.ElementType!));
```

**Step 5: Run to verify still fails (helpers not wired yet)**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_UsesListForModelWithCollectionOfValidateType"
```

Expected: Still FAIL — `EmitValidateBody` not yet changed.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add collection [Validate] element detection helpers to RuleEmitter"
```

---

### Task 2: Emit collection validation loops in `EmitValidateBody`

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

**Step 1: Wire collection detection into `EmitValidateBody`**

Modify the top section of `EmitValidateBody` to include collection properties in the `hasNested` check:

```csharp
var nestedProperties = GetNestedValidateProperties(classSymbol).ToList();
var collectionProperties = GetCollectionValidateProperties(classSymbol).ToList();
bool hasNested = nestedProperties.Count > 0 || collectionProperties.Count > 0;
```

**Step 2: Add the collection emission block inside the `if (hasNested)` branch**

After the existing nested-validator block (after the closing `sb.AppendLine();` of the nested foreach) and before `sb.AppendLine("        return new global::ZeroAlloc.Validation.ValidationResult(failures.ToArray());")`, add:

```csharp
// Collection validators
foreach (var (collProp, elementType) in collectionProperties)
{
    var propName = collProp.Name;
    var varName = char.ToLowerInvariant(propName[0]) + propName.Substring(1);
    var elemNamespace = elementType.ContainingNamespace?.ToDisplayString();
    var elemTypeName = elementType.Name;
    var collValidatorName = $"{elemTypeName}Validator";
    var qualifiedCollValidatorName = string.IsNullOrEmpty(elemNamespace) || elemNamespace == "<global namespace>"
        ? $"global::{collValidatorName}"
        : $"global::{elemNamespace}.{collValidatorName}";

    sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
    sb.AppendLine("        {");
    sb.AppendLine($"            int {varName}Idx = 0;");
    sb.AppendLine($"            foreach (var {varName}Item in {modelParamName}.{propName})");
    sb.AppendLine("            {");
    sb.AppendLine($"                if ({varName}Item is not null)");
    sb.AppendLine("                {");
    sb.AppendLine($"                    var {varName}Result = new {qualifiedCollValidatorName}().Validate({varName}Item);");
    sb.AppendLine($"                    foreach (var f in {varName}Result.Failures)");
    sb.AppendLine($"                        failures.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}[\" + {varName}Idx + \"].\" + f.PropertyName, ErrorMessage = f.ErrorMessage }});");
    sb.AppendLine("                }");
    sb.AppendLine($"                {varName}Idx++;");
    sb.AppendLine("            }");
    sb.AppendLine("        }");
    sb.AppendLine();
}
```

**Step 3: Run to verify the Task 1 test now passes**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_UsesListForModelWithCollectionOfValidateType"
```

Expected: PASS.

**Step 4: Run all tests to confirm no regressions**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

Expected: All tests pass.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs
git commit -m "feat: emit collection validation loops with bracket-indexed property names"
```

---

### Task 3: Generator tests for bracket notation, array support, and null guard

**Files:**
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Add three tests**

```csharp
[Fact]
public void Generator_EmitsCollectionValidation_WithBracketIndex()
{
    var source = """
        using ZeroAlloc.Validation;
        using System.Collections.Generic;
        namespace TestModels;

        [Validate]
        public class LineItem { [NotEmpty] public string Sku { get; set; } = ""; }

        [Validate]
        public class Order { public List<LineItem> Items { get; set; } = new(); }
        """;

    var orderSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("OrderValidator"));

    Assert.Contains("LineItemValidator", orderSource);
    Assert.Contains("\"Items[\" +", orderSource);
    Assert.Contains("is not null", orderSource);
    Assert.Contains("foreach", orderSource);
}

[Fact]
public void Generator_DetectsArrayOfValidateType()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Tag { [NotEmpty] public string Name { get; set; } = ""; }

        [Validate]
        public class Article { public Tag[] Tags { get; set; } = []; }
        """;

    var articleSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("ArticleValidator"));

    Assert.Contains("TagValidator", articleSource);
    Assert.Contains("List<", articleSource);
}

[Fact]
public void Generator_EmitsNullGuard_ForCollectionProperty()
{
    var source = """
        using ZeroAlloc.Validation;
        using System.Collections.Generic;
        namespace TestModels;

        [Validate]
        public class Item { [NotEmpty] public string Name { get; set; } = ""; }

        [Validate]
        public class Bag { public List<Item>? Items { get; set; } }
        """;

    var bagSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("BagValidator"));

    Assert.Contains("is not null", bagSource);
}
```

**Step 2: Run to verify all pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsCollectionValidation|Generator_DetectsArray|Generator_EmitsNullGuard_ForCollection"
```

Expected: All 3 PASS.

**Step 3: Commit**

```bash
git add tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "test: add generator tests for collection validation bracket notation and null guard"
```

---

### Task 4: End-to-end integration tests

**Files:**
- Modify: `tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs`

Append the following models and test class at the end of the file:

```csharp
[Validate]
public class LineItem
{
    [NotEmpty(Message = "SKU is required.")]
    public string Sku { get; set; } = "";

    [GreaterThan(0, Message = "Quantity must be positive.")]
    public int Quantity { get; set; }
}

[Validate]
public class Cart
{
    [NotEmpty]
    public string CustomerId { get; set; } = "";

    public List<LineItem> Items { get; set; } = [];
}

public class CollectionValidationTests
{
    private readonly CartValidator _validator = new();

    [Fact]
    public void Valid_Cart_PassesValidation()
    {
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items =
            [
                new LineItem { Sku = "ABC", Quantity = 2 },
                new LineItem { Sku = "DEF", Quantity = 1 }
            ]
        };
        ValidationAssert.NoErrors(_validator.Validate(cart));
    }

    [Fact]
    public void Item_InvalidSku_ReportsBracketIndexedFailure()
    {
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items = [ new LineItem { Sku = "", Quantity = 1 } ]
        };
        var result = _validator.Validate(cart);
        var failures = result.Failures.ToArray();
        ValidationAssert.HasError(result, "Items[0].Sku");
        Assert.Equal("SKU is required.", failures.Single(f => f.PropertyName == "Items[0].Sku").ErrorMessage);
    }

    [Fact]
    public void SecondItem_Invalid_ReportsCorrectIndex()
    {
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items =
            [
                new LineItem { Sku = "ABC", Quantity = 1 },
                new LineItem { Sku = "", Quantity = 1 }
            ]
        };
        var result = _validator.Validate(cart);
        ValidationAssert.HasError(result, "Items[1].Sku");
        Assert.DoesNotContain(result.Failures.ToArray(), f => f.PropertyName == "Items[0].Sku");
    }

    [Fact]
    public void Multiple_Items_Multiple_Failures_AllReported()
    {
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items =
            [
                new LineItem { Sku = "", Quantity = 0 },
                new LineItem { Sku = "ABC", Quantity = 1 },
                new LineItem { Sku = "", Quantity = -1 }
            ]
        };
        var result = _validator.Validate(cart);
        ValidationAssert.HasError(result, "Items[0].Sku");
        ValidationAssert.HasError(result, "Items[0].Quantity");
        ValidationAssert.HasError(result, "Items[2].Sku");
        ValidationAssert.HasError(result, "Items[2].Quantity");
        Assert.Equal(4, result.Failures.Length);
    }

    [Fact]
    public void Null_Collection_IsSkipped()
    {
        var cart = new Cart { CustomerId = "C-001", Items = null! };
        ValidationAssert.NoErrors(_validator.Validate(cart));
    }

    [Fact]
    public void Direct_And_Collection_Failures_ReportedTogether()
    {
        var cart = new Cart
        {
            CustomerId = "",  // direct failure
            Items = [ new LineItem { Sku = "", Quantity = 1 } ]  // collection failure
        };
        var result = _validator.Validate(cart);
        ValidationAssert.HasError(result, "CustomerId");
        ValidationAssert.HasError(result, "Items[0].Sku");
        Assert.Equal(2, result.Failures.Length);
    }
}
```

**Step 2: Build to verify `CartValidator` and `LineItemValidator` are generated**

```bash
dotnet build tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

Expected: 0 errors. If `CartValidator` is missing, inspect `tests/ZeroAlloc.Validation.Tests/obj/Debug/net10.0/generated/`.

**Step 3: Run collection integration tests**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "CollectionValidationTests"
```

Expected: All 6 tests pass.

**Step 4: Run full suite**

```bash
dotnet test ZeroAlloc.Validation.slnx
```

Expected: All tests pass.

**Step 5: Commit**

```bash
git add tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs
git commit -m "test: add end-to-end integration tests for collection property validation"
```

---

### Task 5: Final verification

**Step 1: Full solution build**

```bash
dotnet build ZeroAlloc.Validation.slnx
```

Expected: 0 errors, 0 warnings.

**Step 2: Full test run**

```bash
dotnet test ZeroAlloc.Validation.slnx
```

Expected: All tests pass across net8.0, net9.0, net10.0.

**Step 3: Final commit**

```bash
git commit --allow-empty -m "chore: collection validation complete"
```
