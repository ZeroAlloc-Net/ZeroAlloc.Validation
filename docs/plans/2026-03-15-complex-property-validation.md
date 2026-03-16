# Complex Property Validation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the source generator automatically validate nested objects whose types are decorated with `[Validate]`, prefixing failure property names with the parent property name using dot notation.

**Architecture:** The generator's `RuleEmitter` is extended to detect properties whose declared type carries `[Validate]`. When any such property exists on a model, the emitted `Validate()` body switches from a fixed `ValidationFailure[]` to a `List<ValidationFailure>`, appends direct rule failures as before, then calls the nested type's generated validator and re-emits each nested failure with a dot-prefixed `PropertyName`. Flat models (no nested `[Validate]` properties) are unaffected.

**Tech Stack:** C# 13, Roslyn `IIncrementalGenerator`, `INamedTypeSymbol`, `Microsoft.CodeAnalysis.CSharp` 4.11.0, xUnit 2.9.3.

---

## Reference

Design doc: `docs/plans/2026-03-15-complex-property-validation-design.md`

Key existing files:
- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` — `EmitValidateBody(StringBuilder, INamedTypeSymbol)` — **modify this**
- `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` — `Emit(SourceProductionContext, INamedTypeSymbol)` — no changes needed
- `src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs` — `ZeroAlloc.Validation.ValidateAttribute`
- `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs` — existing generator unit tests
- `tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs` — existing e2e tests

---

### Task 1: Detect nested `[Validate]` properties in `RuleEmitter`

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write the failing test**

Add to `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_UsesListForModelWithNestedValidateType()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Address
        {
            [NotEmpty]
            public string Street { get; set; } = "";
        }

        [Validate]
        public class Customer
        {
            [NotEmpty]
            public string Name { get; set; } = "";
            public Address Address { get; set; } = new();
        }
        """;

    var customerSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("CustomerValidator"));

    Assert.Contains("List<", customerSource);
    Assert.DoesNotContain("List<", RunGeneratorGetSources(source)
        .First(s => s.Contains("AddressValidator")));
}
```

Also add the helper overload `RunGeneratorGetSources` (returns all generated sources, not just the first):

```csharp
private static IReadOnlyList<string> RunGeneratorGetSources(string source)
{
    var compilation = CSharpCompilation.Create(
        "TestAssembly",
        [CSharpSyntaxTree.ParseText(source)],
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(
                System.IO.Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll")),
        ],
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    var generator = new ValidatorGenerator();
    var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
    return driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
}
```

**Step 2: Run to verify fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_UsesListForModelWithNestedValidateType"
```

Expected: FAIL — currently both validators use the fixed array.

**Step 3: Add `HasNestedValidateProperties` and `GetNestedValidateProperties` helpers to `RuleEmitter.cs`**

Add these private helpers inside `RuleEmitter`:

```csharp
private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

private static bool HasNestedValidateProperties(INamedTypeSymbol classSymbol) =>
    classSymbol.GetMembers()
        .OfType<IPropertySymbol>()
        .Any(p => p.Type is INamedTypeSymbol t && HasValidateAttribute(t));

private static IEnumerable<IPropertySymbol> GetNestedValidateProperties(INamedTypeSymbol classSymbol) =>
    classSymbol.GetMembers()
        .OfType<IPropertySymbol>()
        .Where(p => p.Type is INamedTypeSymbol t && HasValidateAttribute(t));

private static bool HasValidateAttribute(INamedTypeSymbol typeSymbol) =>
    typeSymbol.GetAttributes()
        .Any(a => a.AttributeClass?.ToDisplayString() == ValidateAttributeFqn);
```

**Step 4: Run to verify fails (helpers added but not wired yet)**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_UsesListForModelWithNestedValidateType"
```

Expected: Still FAIL — `EmitValidateBody` not yet changed.

**Step 5: Commit helpers only**

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add nested [Validate] detection helpers to RuleEmitter"
```

---

### Task 2: Switch to `List<ValidationFailure>` when nested properties exist

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

**Step 1: The test from Task 1 is still failing — fix it now**

Modify `EmitValidateBody` in `RuleEmitter.cs` to branch on `HasNestedValidateProperties`:

Replace the current opening of `EmitValidateBody` (the part that emits the fixed array and `count` variable) with:

```csharp
public static void EmitValidateBody(StringBuilder sb, INamedTypeSymbol classSymbol, string modelParamName = "instance")
{
    var byProperty = new List<(IPropertySymbol Property, List<AttributeData> Rules)>();
    foreach (var member in classSymbol.GetMembers())
    {
        if (member is not IPropertySymbol prop) continue;
        var propRules = prop.GetAttributes().Where(IsRuleAttribute).ToList();
        if (propRules.Count > 0)
            byProperty.Add((prop, propRules));
    }

    var nestedProperties = GetNestedValidateProperties(classSymbol).ToList();
    bool hasNested = nestedProperties.Count > 0;
    int totalDirectRules = byProperty.Sum(x => x.Rules.Count);

    if (hasNested)
    {
        // Use List<> — nested validator failure count unknown at compile time
        sb.AppendLine("        var failures = new System.Collections.Generic.List<global::ZeroAlloc.Validation.ValidationFailure>();");
        sb.AppendLine();

        // Direct rules — use failures.Add(...)
        foreach (var (prop, rules) in byProperty)
        {
            var propName = prop.Name;
            var propAccess = $"{modelParamName}.{propName}";

            for (int i = 0; i < rules.Count; i++)
            {
                var attr = rules[i];
                var fqn = attr.AttributeClass!.ToDisplayString();
                var prefix = i == 0 ? "        if" : "        else if";
                var message = GetMessage(attr) ?? GetDefaultMessage(fqn, attr, propName);
                var condition = BuildCondition(fqn, attr, propAccess);

                sb.AppendLine($"{prefix} ({condition})");
                sb.AppendLine($"            failures.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = \"{EscapeString(message)}\" }});");
            }
            sb.AppendLine();
        }

        // Nested validators
        foreach (var nestedProp in nestedProperties)
        {
            var propName = nestedProp.Name;
            var nestedTypeName = nestedProp.Type.Name;
            var validatorName = $"{nestedTypeName}Validator";

            sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var nestedResult = new {validatorName}().Validate({modelParamName}.{propName});");
            sb.AppendLine("            foreach (var f in nestedResult.Failures)");
            sb.AppendLine($"                failures.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}.\" + f.PropertyName, ErrorMessage = f.ErrorMessage }});");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("        return new global::ZeroAlloc.Validation.ValidationResult(failures.ToArray());");
    }
    else
    {
        // Flat model — keep existing fixed array path
        sb.AppendLine($"        var buffer = new global::ZeroAlloc.Validation.ValidationFailure[{totalDirectRules}];");
        sb.AppendLine("        int count = 0;");
        sb.AppendLine();

        foreach (var (prop, rules) in byProperty)
        {
            var propName = prop.Name;
            var propAccess = $"{modelParamName}.{propName}";

            for (int i = 0; i < rules.Count; i++)
            {
                var attr = rules[i];
                var fqn = attr.AttributeClass!.ToDisplayString();
                var prefix = i == 0 ? "        if" : "        else if";
                var message = GetMessage(attr) ?? GetDefaultMessage(fqn, attr, propName);
                var condition = BuildCondition(fqn, attr, propAccess);

                sb.AppendLine($"{prefix} ({condition})");
                sb.AppendLine($"            buffer[count++] = new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = \"{EscapeString(message)}\" }};");
            }
            sb.AppendLine();
        }

        sb.AppendLine("        return new global::ZeroAlloc.Validation.ValidationResult(buffer[..count].ToArray());");
    }
}
```

**Step 2: Run to verify passes**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_UsesListForModelWithNestedValidateType"
```

Expected: PASS.

**Step 3: Run all tests to check no regressions**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

Expected: All tests pass.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs
git commit -m "feat: switch to List<ValidationFailure> for models with nested [Validate] properties"
```

---

### Task 3: Verify nested failures are dot-prefixed

**Files:**
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write the failing test**

Add to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_EmitsNestedValidation_WithDotPrefix()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Address
        {
            [NotEmpty]
            public string Street { get; set; } = "";
        }

        [Validate]
        public class Customer
        {
            public Address Address { get; set; } = new();
        }
        """;

    var customerSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("CustomerValidator"));

    Assert.Contains("AddressValidator", customerSource);
    Assert.Contains("\"Address.\" +", customerSource);
    Assert.Contains("is not null", customerSource);
}

[Fact]
public void Generator_SkipsNestedValidation_WhenPropertyIsNull()
{
    // The generated code must have the null guard
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        [Validate]
        public class Address { [NotEmpty] public string Street { get; set; } = ""; }

        [Validate]
        public class Customer { public Address? Address { get; set; } }
        """;

    var customerSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("CustomerValidator"));

    Assert.Contains("is not null", customerSource);
}
```

**Step 2: Run to verify**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsNestedValidation"
```

Expected: Both PASS (implementation was done in Task 2).

**Step 3: Commit**

```bash
git add tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "test: add generator tests for nested validation dot-prefix and null guard"
```

---

### Task 4: End-to-end integration tests

**Files:**
- Modify: `tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs`

Add a nested model and integration tests to the existing file.

**Step 1: Add the nested model and tests**

Add to `tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs`:

```csharp
[Validate]
public class Address
{
    [NotEmpty(Message = "Street is required.")]
    public string Street { get; set; } = "";

    [NotEmpty(Message = "City is required.")]
    public string City { get; set; } = "";
}

[Validate]
public class Order
{
    [NotEmpty]
    public string Reference { get; set; } = "";

    [NotNull]
    public Address? ShippingAddress { get; set; }

    // Address has [Validate] → automatically nested
    public Address BillingAddress { get; set; } = new();
}

public class NestedValidationTests
{
    private readonly OrderValidator _validator = new();

    [Fact]
    public void Valid_Order_PassesValidation()
    {
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
            BillingAddress = new Address { Street = "456 Oak Ave", City = "Shelbyville" }
        };
        var result = _validator.Validate(order);
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Nested_BillingAddress_Invalid_ReportsDotPrefixedFailure()
    {
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
            BillingAddress = new Address { Street = "", City = "Shelbyville" }
        };
        var result = _validator.Validate(order);
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "BillingAddress.Street");
        Assert.Equal("Street is required.", result.Failures.ToArray()
            .First(f => f.PropertyName == "BillingAddress.Street").ErrorMessage);
    }

    [Fact]
    public void Nested_BillingAddress_Null_IsSkipped()
    {
        // BillingAddress has default value (new Address()), so make it invalid differently
        // This test verifies null skipping by using a nullable ShippingAddress set to null
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = null,  // [NotNull] should fire
            BillingAddress = new Address { Street = "456 Oak Ave", City = "Shelbyville" }
        };
        var result = _validator.Validate(order);
        // ShippingAddress is null → NotNull fires, but no nested failures for ShippingAddress
        ValidationAssert.HasError(result, "ShippingAddress");
        // No "ShippingAddress.Street" type failures since ShippingAddress is null
        Assert.DoesNotContain(result.Failures.ToArray(), f => f.PropertyName.StartsWith("ShippingAddress."));
    }

    [Fact]
    public void Multiple_Nested_Failures_AllReported()
    {
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
            BillingAddress = new Address { Street = "", City = "" }
        };
        var result = _validator.Validate(order);
        ValidationAssert.HasError(result, "BillingAddress.Street");
        ValidationAssert.HasError(result, "BillingAddress.City");
    }

    [Fact]
    public void Direct_And_Nested_Failures_Reported_Together()
    {
        var order = new Order
        {
            Reference = "",  // direct failure
            ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
            BillingAddress = new Address { Street = "", City = "Shelbyville" }  // nested failure
        };
        var result = _validator.Validate(order);
        ValidationAssert.HasError(result, "Reference");
        ValidationAssert.HasError(result, "BillingAddress.Street");
    }
}
```

**Step 2: Build to verify generator produces `OrderValidator` and `AddressValidator`**

```bash
dotnet build tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

Expected: 0 errors. If `OrderValidator` is not found, inspect generated files in `tests/ZeroAlloc.Validation.Tests/obj/Debug/net10.0/generated/`.

**Step 3: Run integration tests**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "NestedValidationTests"
```

Expected: All 5 tests pass.

**Step 4: Run all tests**

```bash
dotnet test ZeroAlloc.Validation.slnx
```

Expected: All tests pass.

**Step 5: Commit**

```bash
git add tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs
git commit -m "test: add end-to-end integration tests for nested property validation"
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
git commit --allow-empty -m "chore: complex property validation complete"
```
