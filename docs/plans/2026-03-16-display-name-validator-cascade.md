# Display Name Override + Validator-Level Cascade Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship §5.5 `[DisplayName]` (human-readable name in error messages) and §7.2 `[Validate(StopOnFirstFailure = true)]` (fail-fast after first failing property).

**Architecture:** Both features touch only `RuleEmitter.cs` (generator) plus one attribute file each. `[DisplayName]` replaces the name string passed to `ResolveMessage`/`GetDefaultMessage` at emit time; cascade wraps each property group in a failure-count check in both flat and nested emit paths.

**Tech Stack:** C# source generator (`IIncrementalGenerator`, `netstandard2.0`), xUnit, `ZeroAlloc.Validation.Testing.ValidationAssert`.

---

### Task 1: `[DisplayName]` attribute + generator support

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/DisplayNameAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/DisplayNameModel.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/DisplayNameTests.cs`

---

**Step 1: Write failing generator emission tests**

Append to `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_DisplayName_UsesDisplayNameInDefaultMessage()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class M
        {
            [DisplayName("First Name")]
            [NotEmpty]
            public string Forename { get; set; } = "";
        }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("\"First Name must not be empty.\"", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("\"Forename must not be empty.\"", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_DisplayName_SubstitutesPropertyNamePlaceholder()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class M
        {
            [DisplayName("ZIP Code")]
            [Matches(@"^\d{5}$", Message = "{PropertyName} must be 5 digits.")]
            public string ZipCode { get; set; } = "";
        }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("\"ZIP Code must be 5 digits.\"", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("\"ZipCode must be 5 digits.\"", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_NoDisplayName_UsesRawPropertyName()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class M
        {
            [NotEmpty]
            public string Forename { get; set; } = "";
        }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("\"Forename must not be empty.\"", generated, StringComparison.Ordinal);
}
```

**Step 2: Run tests, verify they FAIL**

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "DisplayName" -v minimal
```

Expected: 3 failures (attribute not yet defined, generator not yet updated).

**Step 3: Create the attribute**

`src/ZeroAlloc.Validation/Attributes/DisplayNameAttribute.cs`:

```csharp
namespace ZeroAlloc.Validation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class DisplayNameAttribute : Attribute
{
    public DisplayNameAttribute(string displayName)
    {
        DisplayName = displayName;
    }

    public string DisplayName { get; }
}
```

**Step 4: Add generator support in `RuleEmitter.cs`**

4a. Add FQN constant near the top of `RuleEmitter` (after `StopOnFirstFailureFqn`):

```csharp
private const string DisplayNameAttributeFqn = "ZeroAlloc.Validation.DisplayNameAttribute";
```

4b. Add `GetDisplayName` helper (after `GetUnless`):

```csharp
private static string? GetDisplayName(IPropertySymbol prop)
{
    foreach (var attr in prop.GetAttributes())
    {
        if (!string.Equals(attr.AttributeClass?.ToDisplayString(), DisplayNameAttributeFqn, StringComparison.Ordinal))
            continue;
        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string s)
            return s;
    }
    return null;
}
```

4c. In `EmitPropertyRulesWithAdd`, add `displayName` and pass it to message resolution (two places that currently use `propName` for messages):

```csharp
var propName = prop.Name;
var displayName = GetDisplayName(prop) ?? propName;   // ← add this line
var propAccess = $"{modelParamName}.{propName}";
// ...
var message = ResolveMessage(attr, fqn, displayName) ?? GetDefaultMessage(fqn, attr, displayName);
//                                       ^^^^^^^^^^^                                  ^^^^^^^^^^^
```

4d. Apply the same `displayName` change in `EmitFlatPath` (identical structure, same two lines to update).

> Note: `PropertyName = \"{propName}\"` in `BuildFailureInitializer` must remain `propName` — the raw C# name, not the display name.

**Step 5: Run generator tests, verify they PASS**

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "DisplayName" -v minimal
```

Expected: all 3 pass.

**Step 6: Write integration tests — model**

`tests/ZeroAlloc.Validation.Tests/Integration/DisplayNameModel.cs`:

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class DisplayNameModel
{
    [DisplayName("First Name")]
    [NotEmpty]
    [MinLength(2)]
    public string Forename { get; set; } = "ok";

    [DisplayName("ZIP Code")]
    [Matches(@"^\d{5}$", Message = "{PropertyName} must be 5 digits.")]
    public string ZipCode { get; set; } = "12345";

    // No [DisplayName] — raw property name used
    [NotEmpty]
    public string NoDisplayName { get; set; } = "ok";
}
```

**Step 7: Write integration tests — test class**

`tests/ZeroAlloc.Validation.Tests/Integration/DisplayNameTests.cs`:

```csharp
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class DisplayNameTests
{
    private readonly DisplayNameModelValidator _validator = new();

    [Fact]
    public void DisplayName_AppearsInDefaultMessage()
    {
        var model = new DisplayNameModel { Forename = "" };
        var result = _validator.Validate(model);
        ValidationAssert.HasErrorWithMessage(result, "Forename", "First Name must not be empty.");
    }

    [Fact]
    public void DisplayName_AppearsForAllRulesOnProperty()
    {
        var model = new DisplayNameModel { Forename = "x" };  // passes NotEmpty, fails MinLength(2)
        var result = _validator.Validate(model);
        ValidationAssert.HasErrorWithMessage(result, "Forename", "First Name must be at least 2 characters.");
    }

    [Fact]
    public void DisplayName_SubstitutesPropertyNamePlaceholder()
    {
        var model = new DisplayNameModel { ZipCode = "abc" };
        var result = _validator.Validate(model);
        ValidationAssert.HasErrorWithMessage(result, "ZipCode", "ZIP Code must be 5 digits.");
    }

    [Fact]
    public void DisplayName_PropertyNameInFailureIsRawCSharpName()
    {
        var model = new DisplayNameModel { Forename = "" };
        var result = _validator.Validate(model);
        // ValidationFailure.PropertyName must stay "Forename", not "First Name"
        ValidationAssert.HasError(result, "Forename");
    }

    [Fact]
    public void NoDisplayName_UsesRawPropertyName()
    {
        var model = new DisplayNameModel { NoDisplayName = "" };
        var result = _validator.Validate(model);
        ValidationAssert.HasErrorWithMessage(result, "NoDisplayName", "NoDisplayName must not be empty.");
    }
}
```

**Step 8: Run all tests, verify they PASS**

```
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all tests pass (count increases by 8 = 3 generator + 5 integration).

**Step 9: Commit**

```bash
git add src/ZeroAlloc.Validation/Attributes/DisplayNameAttribute.cs \
        src/ZeroAlloc.Validation.Generator/RuleEmitter.cs \
        tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs \
        tests/ZeroAlloc.Validation.Tests/Integration/DisplayNameModel.cs \
        tests/ZeroAlloc.Validation.Tests/Integration/DisplayNameTests.cs
git commit -m "feat: add [DisplayName] attribute for display-name override in error messages"
```

---

### Task 2: Validator-level cascade (`[Validate(StopOnFirstFailure = true)]`)

**Files:**
- Modify: `src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/ValidatorCascadeModel.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/ValidatorCascadeTests.cs`

---

**Step 1: Write failing generator emission tests**

Append to `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_ValidatorStop_FlatPath_EmitsCountCheckAfterEachPropertyGroup()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate(StopOnFirstFailure = true)]
        public class M
        {
            [NotEmpty]
            public string Name { get; set; } = "";

            [GreaterThan(0)]
            public int Age { get; set; }
        }
        """;

    var generated = RunGeneratorGetSource(source);
    // Each property group gets a count snapshot and an early-return check
    Assert.Contains("_b0 = count", generated, StringComparison.Ordinal);
    Assert.Contains("count > _b0", generated, StringComparison.Ordinal);
    Assert.Contains("_b1 = count", generated, StringComparison.Ordinal);
    Assert.Contains("count > _b1", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_ValidatorStop_NestedPath_EmitsFailuresCountCheckAfterEachPropertyGroup()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Inner { [NotEmpty] public string X { get; set; } = ""; }

        [Validate(StopOnFirstFailure = true)]
        public class Outer
        {
            [NotEmpty]
            public string Reference { get; set; } = "";

            public Inner? Item { get; set; }
        }
        """;

    var generated = RunGeneratorGetSources(source)
        .First(s => s.Contains("class OuterValidator"));
    Assert.Contains("_b0 = failures.Count", generated, StringComparison.Ordinal);
    Assert.Contains("failures.Count > _b0", generated, StringComparison.Ordinal);
    Assert.Contains("_b1 = failures.Count", generated, StringComparison.Ordinal);
    Assert.Contains("failures.Count > _b1", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_ValidatorStop_Default_NoCountChecks()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class M
        {
            [NotEmpty]
            public string Name { get; set; } = "";

            [GreaterThan(0)]
            public int Age { get; set; }
        }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.DoesNotContain("_b0 =", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("_b1 =", generated, StringComparison.Ordinal);
}
```

> Note: `RunGeneratorGetSources` (plural) is needed for multi-class tests — check existing tests in the file for how it is called. It returns `IEnumerable<string>` of generated sources.

**Step 2: Run tests, verify they FAIL**

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "ValidatorStop" -v minimal
```

Expected: 3 failures (property not yet on attribute, generator not yet updated).

**Step 3: Add `StopOnFirstFailure` to `ValidateAttribute.cs`**

```csharp
namespace ZeroAlloc.Validation;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ValidateAttribute : Attribute
{
    public bool StopOnFirstFailure { get; set; }
}
```

**Step 4: Update `RuleEmitter.cs`**

4a. Add `GetBoolNamedArg` helper (after `GetSeverityValue`):

```csharp
private static bool GetBoolNamedArg(AttributeData? attr, string name)
{
    if (attr is null) return false;
    foreach (var named in attr.NamedArguments)
        if (string.Equals(named.Key, name, StringComparison.Ordinal) && named.Value.Value is bool b)
            return b;
    return false;
}
```

4b. Update `EmitValidateBody` to read the flag and forward it:

```csharp
public static void EmitValidateBody(StringBuilder sb, INamedTypeSymbol classSymbol, string modelParamName = "instance")
{
    var byProperty = CollectPropertyRules(classSymbol);
    var nestedProperties = GetNestedValidateProperties(classSymbol).ToList();
    var collectionProperties = GetCollectionValidateProperties(classSymbol).ToList();
    bool hasNested = nestedProperties.Count > 0 || collectionProperties.Count > 0;
    int totalDirectRules = byProperty.Sum(x => x.Rules.Count);

    var validateAttr = classSymbol.GetAttributes()
        .FirstOrDefault(a => string.Equals(a.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, StringComparison.Ordinal));
    bool validatorStop = GetBoolNamedArg(validateAttr, "StopOnFirstFailure");

    if (hasNested)
        EmitNestedPath(sb, classSymbol, byProperty, nestedProperties, collectionProperties, modelParamName, validatorStop);
    else
        EmitFlatPath(sb, byProperty, totalDirectRules, modelParamName, validatorStop);
}
```

4c. Update `EmitFlatPath` signature to accept `bool validatorStop` and emit count checks around each property group:

```csharp
private static void EmitFlatPath(
    StringBuilder sb,
    List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
    int totalDirectRules,
    string modelParamName,
    bool validatorStop)
{
    sb.AppendLine($"        var buffer = new global::ZeroAlloc.Validation.ValidationFailure[{totalDirectRules}];");
    sb.AppendLine("        int count = 0;");
    sb.AppendLine();

    for (int pi = 0; pi < byProperty.Count; pi++)
    {
        var (prop, rules) = byProperty[pi];
        var propName = prop.Name;
        var displayName = GetDisplayName(prop) ?? propName;
        var propAccess = $"{modelParamName}.{propName}";
        var stopMode = HasStopOnFirstFailure(prop);

        if (validatorStop)
            sb.AppendLine($"        int _b{pi} = count;");

        for (int i = 0; i < rules.Count; i++)
        {
            var attr = rules[i];
            var fqn = attr.AttributeClass!.ToDisplayString();
            var prefix = (stopMode && i > 0) ? "        else if" : "        if";
            var message = ResolveMessage(attr, fqn, displayName) ?? GetDefaultMessage(fqn, attr, displayName);
            var propTypeFullName = GetNullableUnwrappedFullTypeName(prop);
            var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName, modelParamName);
            var propertyValueExpr = HasPropertyValuePlaceholder(message) ? BuildPropertyValueExpr(prop, modelParamName) : null;
            var whenMethod   = GetWhen(attr);
            var unlessMethod = GetUnless(attr);
            var whenGuard    = whenMethod   is null ? "" : $"{modelParamName}.{whenMethod}() && ";
            var unlessGuard  = unlessMethod is null ? "" : $"!{modelParamName}.{unlessMethod}() && ";

            sb.AppendLine($"{prefix} ({whenGuard}{unlessGuard}{condition})");
            sb.AppendLine($"            buffer[count++] = {BuildFailureInitializer(propName, message, attr, propertyValueExpr)};");
        }

        if (validatorStop)
        {
            sb.AppendLine($"        if (count > _b{pi})");
            sb.AppendLine("        {");
            sb.AppendLine("            var r = new global::ZeroAlloc.Validation.ValidationFailure[count];");
            sb.AppendLine("            global::System.Array.Copy(buffer, r, count);");
            sb.AppendLine("            return new global::ZeroAlloc.Validation.ValidationResult(r);");
            sb.AppendLine("        }");
        }

        sb.AppendLine();
    }

    sb.AppendLine("        if (count == buffer.Length) return new global::ZeroAlloc.Validation.ValidationResult(buffer);");
    sb.AppendLine("        var result = new global::ZeroAlloc.Validation.ValidationFailure[count];");
    sb.AppendLine("        global::System.Array.Copy(buffer, result, count);");
    sb.AppendLine("        return new global::ZeroAlloc.Validation.ValidationResult(result);");
}
```

4d. Update `EmitNestedPath` signature to accept `classSymbol` and `bool validatorStop`, and use a unified per-property loop when `validatorStop = true`.

When `validatorStop = false`, behavior is identical to today (call `EmitPropertyRulesWithAdd`, `EmitNestedValidators`, `EmitCollectionValidators` in that order).

When `validatorStop = true`, iterate `classSymbol.GetMembers()` in declaration order and for each property:
- Snapshot `int _b{groupIdx} = failures.Count;`
- If property has direct rules: emit them (same logic as `EmitPropertyRulesWithAdd` for that single property)
- If property is a nested validator property: emit the null guard + nested call
- If property is a collection validator property: emit the collection loop
- Emit `if (failures.Count > _b{groupIdx}) return new global::ZeroAlloc.Validation.ValidationResult(failures.ToArray());`

This ensures declaration order is respected even when some properties have only nested/collection validation. Use `SymbolEqualityComparer.Default.Equals` to match properties across the pre-computed lists.

The collection loop variable names (`_c{ci}Idx`, `_c{ci}Item`) need a separate counter `collCi` incremented once per collection property encountered in the loop.

Updated `EmitNestedPath` signature:

```csharp
private static void EmitNestedPath(
    StringBuilder sb,
    INamedTypeSymbol classSymbol,
    List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
    List<IPropertySymbol> nestedProperties,
    List<(IPropertySymbol Property, INamedTypeSymbol ElementType)> collectionProperties,
    string modelParamName,
    bool validatorStop)
```

**Step 5: Run generator tests, verify they PASS**

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "ValidatorStop" -v minimal
```

Expected: all 3 pass.

**Step 6: Write integration tests — models**

`tests/ZeroAlloc.Validation.Tests/Integration/ValidatorCascadeModel.cs`:

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

// Flat-path model (no nested validators)
[Validate(StopOnFirstFailure = true)]
public class ValidatorCascadeModel
{
    [NotEmpty]
    public string Reference { get; set; } = "ok";

    [GreaterThan(0)]
    public int Quantity { get; set; } = 1;
}

// Nested-path model (uses nested validator)
[Validate(StopOnFirstFailure = true)]
public class ValidatorCascadeWithNestedModel
{
    [NotEmpty]
    public string Reference { get; set; } = "ok";

    public Address? ShippingAddress { get; set; }
}
```

> `Address` is an existing class in the Integration test project with `[Validate]`.

**Step 7: Write integration tests — test class**

`tests/ZeroAlloc.Validation.Tests/Integration/ValidatorCascadeTests.cs`:

```csharp
using System.Linq;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class ValidatorCascadeTests
{
    private readonly ValidatorCascadeModelValidator _flatValidator = new();
    private readonly ValidatorCascadeWithNestedModelValidator _nestedValidator = new(new AddressValidator());

    // --- Flat path ---

    [Fact]
    public void FlatPath_FirstPropertyFails_SecondNotValidated()
    {
        var model = new ValidatorCascadeModel { Reference = "", Quantity = -1 };
        var result = _flatValidator.Validate(model);
        ValidationAssert.HasError(result, "Reference");
        Assert.DoesNotContain(result.Failures, f => f.PropertyName == "Quantity");
    }

    [Fact]
    public void FlatPath_FirstPropertyPasses_SecondValidated()
    {
        var model = new ValidatorCascadeModel { Reference = "ok", Quantity = -1 };
        var result = _flatValidator.Validate(model);
        ValidationAssert.HasError(result, "Quantity");
    }

    [Fact]
    public void FlatPath_AllPass_NoFailures()
    {
        var model = new ValidatorCascadeModel { Reference = "ok", Quantity = 5 };
        var result = _flatValidator.Validate(model);
        ValidationAssert.NoErrors(result);
    }

    // --- Nested path ---

    [Fact]
    public void NestedPath_FirstPropertyFails_NestedNotValidated()
    {
        // Reference fails → ShippingAddress nested validator should not run
        var model = new ValidatorCascadeWithNestedModel
        {
            Reference = "",
            ShippingAddress = new Address { Street = "" }  // invalid, but should not be reached
        };
        var result = _nestedValidator.Validate(model);
        ValidationAssert.HasError(result, "Reference");
        Assert.DoesNotContain(result.Failures, f => f.PropertyName.StartsWith("ShippingAddress"));
    }

    [Fact]
    public void NestedPath_FirstPropertyPasses_NestedValidated()
    {
        var model = new ValidatorCascadeWithNestedModel
        {
            Reference = "ok",
            ShippingAddress = new Address { Street = "" }  // invalid
        };
        var result = _nestedValidator.Validate(model);
        Assert.Contains(result.Failures, f => f.PropertyName.StartsWith("ShippingAddress"));
    }
}
```

> Check `Address.cs` to confirm the property name and which rules apply, and adjust the test accordingly if `Street` is not the correct property.

**Step 8: Run all tests, verify they PASS**

```
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all tests pass (count increases by ~8 = 3 generator + 5 integration).

**Step 9: Commit**

```bash
git add src/ZeroAlloc.Validation/Attributes/ValidateAttribute.cs \
        src/ZeroAlloc.Validation.Generator/RuleEmitter.cs \
        tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs \
        tests/ZeroAlloc.Validation.Tests/Integration/ValidatorCascadeModel.cs \
        tests/ZeroAlloc.Validation.Tests/Integration/ValidatorCascadeTests.cs
git commit -m "feat: add [Validate(StopOnFirstFailure = true)] for validator-level fail-fast cascade"
```

---

### Task 3: Update features.md

**Files:**
- Modify: `docs/features.md`

**Step 1: Mark both features as done**

In `docs/features.md`:
- §5.5: change `WithName` / Display Name Override ⬜` → `### 5.5 `[DisplayName]` ✅`
- §7.2: change `Validator-Level Cascade ⬜` → `### 7.2 Validator-Level Cascade ✅`

Update the description of §5.5 to show the attribute syntax:

```markdown
### 5.5 `[DisplayName]` ✅

Override the property name used in error messages without affecting `ValidationFailure.PropertyName`:

```csharp
[DisplayName("First Name")]
[NotEmpty]
[MinLength(2)]
public string Forename { get; set; } = "";
// → "First Name must not be empty."
// ValidationFailure.PropertyName = "Forename" (unchanged)
```

Update the description of §7.2:

```markdown
### 7.2 Validator-Level Cascade ✅

Stop validating further properties after the first one produces any failure (`StopOnFirstFailure = true` on `[Validate]`):

```csharp
[Validate(StopOnFirstFailure = true)]
public class Order
{
    [NotEmpty]
    public string Reference { get; set; } = "";   // fails → returns immediately

    [NotNull]
    public Address? ShippingAddress { get; set; }  // only reached if Reference passed
}
```

Composes correctly with property-level `[StopOnFirstFailure]`.
```

**Step 2: Run all tests, verify no regressions**

```
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

**Step 3: Commit**

```bash
git add docs/features.md
git commit -m "docs: mark §5.5 DisplayName and §7.2 validator-level cascade as done in features.md"
```
