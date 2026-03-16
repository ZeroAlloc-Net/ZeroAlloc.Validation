# Placeholders, `[Must]`, and `[When]`/`[Unless]` Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add gen-time message placeholders, a `[Must]` predicate attribute, and `When`/`Unless` conditional named params to every validation attribute.

**Architecture:** All three features are generator-only or attribute-only changes — no runtime types added. Placeholders are resolved by a new `ResolveMessage` helper in `RuleEmitter.cs`. `[Must]` is a new attribute with a `BuildCondition` case that emits `!instance.Method(value)`. `When`/`Unless` are two new named params on `ValidationAttribute` that wrap the emitted condition.

**Tech Stack:** C# 12, Roslyn `IIncrementalGenerator`, xUnit, `TreatWarningsAsErrors=true`, MA0048 (one type per file)

---

## Context for implementer

### Project layout

```
src/
  ZeroAlloc.Validation/
    Attributes/ValidationAttribute.cs   ← base class with Message property
    Attributes/NotEmptyAttribute.cs     ← typical attribute (no constructor args)
    Attributes/GreaterThanAttribute.cs  ← typical attribute (double constructor arg)
    Internal/DecimalValidator.cs        ← runtime helper
  ZeroAlloc.Validation.Generator/
    RuleEmitter.cs                      ← all generator logic lives here
tests/
  ZeroAlloc.Validation.Tests/
    Attributes/AttributeDeclarationTests.cs
    Generator/GeneratorRuleEmissionTests.cs
    Integration/RangeModel.cs           ← example model file (one type)
    Integration/RangeTests.cs           ← example test file (one type)
```

### Key rules
- `TreatWarningsAsErrors=true` — zero warnings allowed
- MA0048 — **one top-level type per file**, always
- Every `[Fact]` lives in its own class or in an existing single-class file
- Run `dotnet test` after every task; it builds all three TFMs (net8, net9, net10)

### How the generator works

`RuleEmitter.EmitValidateBody` collects all rule attributes from each property and emits:

```csharp
// flat path (no nested validators):
var buffer = new ValidationFailure[N];
int count = 0;
if (failureCondition1)
    buffer[count++] = new ValidationFailure { PropertyName = "X", ErrorMessage = "..." };
else if (failureCondition2)
    buffer[count++] = new ValidationFailure { PropertyName = "X", ErrorMessage = "..." };
// return buffer slice
```

The message is resolved by:
```csharp
var message = GetMessage(attr) ?? GetDefaultMessage(fqn, attr, propName);
```

`GetMessage` reads the `Message` named argument from the attribute data.

---

## Task 1: `[Must]` attribute + generator + tests

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/MustAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Attributes/AttributeDeclarationTests.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/MustModel.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/MustTests.cs`

---

### Step 1: Write the failing generator test

Add this `[Fact]` to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_EmitsMust_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Widget
        {
            [Must(nameof(IsValidCode))]
            public string Code { get; set; } = "";
            public bool IsValidCode(string value) => value.StartsWith("W", System.StringComparison.Ordinal);
        }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("!instance.IsValidCode(", generated, StringComparison.Ordinal);
}
```

### Step 2: Run test — verify it fails

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "Generator_EmitsMust_Check" -v normal
```

Expected: FAIL (the generator emits `false` for unknown FQNs, so the condition won't contain `IsValidCode`).

### Step 3: Create `MustAttribute.cs`

```csharp
namespace ZeroAlloc.Validation;

public sealed class MustAttribute(string methodName) : ValidationAttribute
{
    public string MethodName { get; } = methodName;
}
```

### Step 4: Update `RuleEmitter.cs`

**4a. Add FQN constant** (after the `PrecisionScaleFqn` line):

```csharp
private const string MustFqn = "ZeroAlloc.Validation.MustAttribute";
```

**4b. Register in `IsRuleAttribute`** — add `or MustFqn` to the return expression.

**4c. Add `modelParamName` parameter to `BuildCondition`:**

Change signature from:
```csharp
private static string BuildCondition(string fqn, AttributeData attr, string access, string propTypeFullName = "")
```
to:
```csharp
private static string BuildCondition(string fqn, AttributeData attr, string access, string propTypeFullName = "", string modelParamName = "instance")
```

**4d. Add Must case** in `BuildCondition` switch (before the `_` fallthrough):
```csharp
MustFqn => $"!{modelParamName}.{GetStringArg(attr, 0)}({access})",
```

**4e. Update both call sites** of `BuildCondition` to pass `modelParamName`:

In `EmitPropertyRulesWithAdd`:
```csharp
var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName, modelParamName);
```

In the loop inside `EmitFlatPath`:
```csharp
var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName, modelParamName);
```

**4f. Add default message** in `GetDefaultMessage` switch (before the `_` fallthrough):
```csharp
MustFqn => $"{propName} is invalid.",
```

### Step 5: Run generator test — verify it passes

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "Generator_EmitsMust_Check" -v normal
```

Expected: PASS.

### Step 6: Write the failing integration test

Create `tests/ZeroAlloc.Validation.Tests/Integration/MustModel.cs`:

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public partial class MustModel
{
    [Must(nameof(CodeStartsWithW), Message = "Code must start with W")]
    public string Code { get; set; } = "";

    public bool CodeStartsWithW(string value) => value.StartsWith("W", System.StringComparison.Ordinal);
}
```

Create `tests/ZeroAlloc.Validation.Tests/Integration/MustTests.cs`:

```csharp
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class MustTests
{
    private readonly MustModelValidator _validator = new();

    [Fact]
    public void Must_ValidValue_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new MustModel { Code = "WIDGET" }));
    }

    [Fact]
    public void Must_InvalidValue_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MustModel { Code = "INVALID" }), "Code");
    }

    [Fact]
    public void Must_CustomMessage_Propagated()
    {
        var result = _validator.Validate(new MustModel { Code = "INVALID" });
        ValidationAssert.HasErrorWithMessage(result, "Code", "Code must start with W");
    }
}
```

### Step 7: Run integration tests — verify they fail

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "MustTests" -v normal
```

Expected: FAIL (MustModelValidator doesn't exist yet — generator hasn't run on the model).

### Step 8: Make `MustModel` partial so the generator can emit the validator

The model is already `partial` in Step 6. Run a full build:

```
dotnet build
```

Expected: succeeds, generator emits `MustModelValidator`.

### Step 9: Run integration tests — verify they pass

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "MustTests" -v normal
```

Expected: all 3 PASS.

### Step 10: Add attribute declaration tests

Add to `AttributeDeclarationTests.cs`:

```csharp
[Fact]
public void MustAttribute_StoresMethodName()
{
    var attr = new MustAttribute("MyMethod");
    Assert.Equal("MyMethod", attr.MethodName);
}

[Fact]
public void MustAttribute_WhenAndUnlessDefaultToNull()
{
    var attr = new MustAttribute("MyMethod");
    Assert.Null(attr.Message);
    Assert.Null(attr.When);
    Assert.Null(attr.Unless);
}
```

> **Note:** `When` and `Unless` don't exist yet — these tests will fail until Task 2 is done. Write them now, but they will pass after Task 2.

Actually — to avoid breaking the build, defer the `When`/`Unless` assertion to Task 2. For now add only:

```csharp
[Fact]
public void MustAttribute_StoresMethodName()
{
    var attr = new MustAttribute("MyMethod");
    Assert.Equal("MyMethod", attr.MethodName);
}

[Fact]
public void MustAttribute_MessageDefaultsToNull()
{
    var attr = new MustAttribute("MyMethod");
    Assert.Null(attr.Message);
}
```

### Step 11: Run all tests

```
dotnet test
```

Expected: all tests pass, 0 warnings.

### Step 12: Commit

```
git add src/ZeroAlloc.Validation/Attributes/MustAttribute.cs
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs
git add tests/ZeroAlloc.Validation.Tests/Attributes/AttributeDeclarationTests.cs
git add tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git add tests/ZeroAlloc.Validation.Tests/Integration/MustModel.cs
git add tests/ZeroAlloc.Validation.Tests/Integration/MustTests.cs
git commit -m "feat: add [Must] validation attribute with instance method predicate"
```

---

## Task 2: `When`/`Unless` named params + generator + tests

**Files:**
- Modify: `src/ZeroAlloc.Validation/Attributes/ValidationAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Attributes/AttributeDeclarationTests.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/ConditionalModel.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/ConditionalTests.cs`

---

### Step 1: Write the failing generator tests

Add to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_EmitsWhen_Guard()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Order
        {
            public bool NeedsShipping { get; set; }
            [NotNull(When = nameof(IsShippingRequired))]
            public string? ShippingAddress { get; set; }
            public bool IsShippingRequired() => NeedsShipping;
        }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("instance.IsShippingRequired() &&", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_EmitsUnless_Guard()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Profile
        {
            public bool IsGuest { get; set; }
            [NotEmpty(Unless = nameof(AllowEmpty))]
            public string Name { get; set; } = "";
            public bool AllowEmpty() => IsGuest;
        }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("!instance.AllowEmpty() &&", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_EmitsBothWhenAndUnless_Guards()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Doc
        {
            public bool IsPublished { get; set; }
            public bool AllowShortTitle { get; set; }
            [MinLength(10, When = nameof(CheckTitle), Unless = nameof(ShortTitleOk))]
            public string Title { get; set; } = "";
            public bool CheckTitle() => IsPublished;
            public bool ShortTitleOk() => AllowShortTitle;
        }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("instance.CheckTitle() &&", generated, StringComparison.Ordinal);
    Assert.Contains("!instance.ShortTitleOk() &&", generated, StringComparison.Ordinal);
}
```

### Step 2: Run — verify they fail

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "Generator_EmitsWhen_Guard|Generator_EmitsUnless_Guard|Generator_EmitsBothWhenAndUnless_Guards" -v normal
```

Expected: all FAIL.

### Step 3: Add `When` and `Unless` to `ValidationAttribute`

```csharp
namespace ZeroAlloc.Validation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class ValidationAttribute : Attribute
{
    public string? Message { get; set; }
    public string? When    { get; set; }
    public string? Unless  { get; set; }
}
```

### Step 4: Add generator helpers and update emit loops in `RuleEmitter.cs`

**4a. Add two private helpers** (alongside `GetMessage`):

```csharp
private static string? GetWhen(AttributeData attr)
{
    foreach (var named in attr.NamedArguments)
        if (string.Equals(named.Key, "When", StringComparison.Ordinal) && named.Value.Value is string s)
            return s;
    return null;
}

private static string? GetUnless(AttributeData attr)
{
    foreach (var named in attr.NamedArguments)
        if (string.Equals(named.Key, "Unless", StringComparison.Ordinal) && named.Value.Value is string s)
            return s;
    return null;
}
```

**4b. Update `EmitPropertyRulesWithAdd`** — replace the condition emission line:

Before:
```csharp
sb.AppendLine($"{prefix} ({condition})");
```

After:
```csharp
var whenMethod   = GetWhen(attr);
var unlessMethod = GetUnless(attr);
var whenGuard    = whenMethod   is null ? "" : $"{modelParamName}.{whenMethod}() && ";
var unlessGuard  = unlessMethod is null ? "" : $"!{modelParamName}.{unlessMethod}() && ";
sb.AppendLine($"{prefix} ({whenGuard}{unlessGuard}{condition})");
```

**4c. Apply identical change** to the emit loop inside `EmitFlatPath` (same pattern, same variables — the loop already has `modelParamName` in scope after Task 1).

### Step 5: Run generator tests — verify they pass

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "Generator_EmitsWhen_Guard|Generator_EmitsUnless_Guard|Generator_EmitsBothWhenAndUnless_Guards" -v normal
```

Expected: all PASS.

### Step 6: Write failing integration tests

Create `tests/ZeroAlloc.Validation.Tests/Integration/ConditionalModel.cs`:

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public partial class ConditionalModel
{
    public bool IsActive { get; set; }
    public bool AllowShortName { get; set; }

    [NotEmpty(When = nameof(ActiveCheck))]
    public string? ActiveName { get; set; }

    [MinLength(5, Unless = nameof(ShortNameOk))]
    public string ShortName { get; set; } = "";

    [NotNull(When = nameof(ActiveCheck), Unless = nameof(ShortNameOk))]
    public string? BothGuard { get; set; }

    public bool ActiveCheck() => IsActive;
    public bool ShortNameOk() => AllowShortName;
}
```

Create `tests/ZeroAlloc.Validation.Tests/Integration/ConditionalTests.cs`:

```csharp
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class ConditionalTests
{
    private readonly ConditionalModelValidator _validator = new();

    [Fact]
    public void When_ConditionFalse_RuleSkipped()
    {
        // IsActive = false → [NotEmpty(When)] is skipped even though ActiveName is empty
        ValidationAssert.NoErrors(_validator.Validate(new ConditionalModel
        {
            IsActive = false,
            ActiveName = null,
            ShortName = "ab",      // would fail MinLength unless AllowShortName
            AllowShortName = true,
            BothGuard = null
        }));
    }

    [Fact]
    public void When_ConditionTrue_RuleTriggered()
    {
        ValidationAssert.HasError(_validator.Validate(new ConditionalModel
        {
            IsActive = true,
            ActiveName = null,     // fails [NotEmpty(When = "ActiveCheck")]
            ShortName = "hello",
            AllowShortName = false,
            BothGuard = "x"
        }), "ActiveName");
    }

    [Fact]
    public void Unless_ConditionTrue_RuleSkipped()
    {
        // AllowShortName = true → [MinLength(Unless)] is skipped
        ValidationAssert.NoErrors(_validator.Validate(new ConditionalModel
        {
            IsActive = false,
            ActiveName = "x",
            ShortName = "ab",      // short, but Unless condition is true so skipped
            AllowShortName = true,
            BothGuard = "x"
        }));
    }

    [Fact]
    public void Unless_ConditionFalse_RuleTriggered()
    {
        ValidationAssert.HasError(_validator.Validate(new ConditionalModel
        {
            IsActive = false,
            ActiveName = "x",
            ShortName = "ab",      // fails [MinLength(5, Unless)] when AllowShortName=false
            AllowShortName = false,
            BothGuard = "x"
        }), "ShortName");
    }

    [Fact]
    public void BothGuards_WhenFalse_RuleSkipped()
    {
        ValidationAssert.NoErrors(_validator.Validate(new ConditionalModel
        {
            IsActive = false,      // When=false → rule skipped regardless of Unless
            ActiveName = "x",
            ShortName = "hello",
            AllowShortName = false,
            BothGuard = null
        }));
    }

    [Fact]
    public void BothGuards_WhenTrueUnlessFalse_RuleTriggered()
    {
        ValidationAssert.HasError(_validator.Validate(new ConditionalModel
        {
            IsActive = true,
            ActiveName = "x",
            ShortName = "hello",
            AllowShortName = false, // Unless=false → rule active
            BothGuard = null        // fails [NotNull(When, Unless)]
        }), "BothGuard");
    }

    [Fact]
    public void BothGuards_WhenTrueUnlessTrue_RuleSkipped()
    {
        ValidationAssert.NoErrors(_validator.Validate(new ConditionalModel
        {
            IsActive = true,
            ActiveName = "x",
            ShortName = "hello",
            AllowShortName = true,  // Unless=true → rule skipped
            BothGuard = null
        }));
    }
}
```

### Step 7: Build and run integration tests

```
dotnet build
dotnet test tests/ZeroAlloc.Validation.Tests --filter "ConditionalTests" -v normal
```

Expected: all 7 PASS.

### Step 8: Add attribute declaration tests

Add to `AttributeDeclarationTests.cs`:

```csharp
[Fact]
public void ValidationAttribute_WhenDefaultsToNull()
{
    var attr = new NotEmptyAttribute();
    Assert.Null(attr.When);
}

[Fact]
public void ValidationAttribute_UnlessDefaultsToNull()
{
    var attr = new NotEmptyAttribute();
    Assert.Null(attr.Unless);
}

[Fact]
public void ValidationAttribute_CanSetWhen()
{
    var attr = new NotEmptyAttribute { When = "IsActive" };
    Assert.Equal("IsActive", attr.When);
}

[Fact]
public void ValidationAttribute_CanSetUnless()
{
    var attr = new NotEmptyAttribute { Unless = "IsGuest" };
    Assert.Equal("IsGuest", attr.Unless);
}

[Fact]
public void MustAttribute_WhenDefaultsToNull()
{
    var attr = new MustAttribute("MyMethod");
    Assert.Null(attr.When);
}
```

### Step 9: Run all tests

```
dotnet test
```

Expected: all pass, 0 warnings.

### Step 10: Commit

```
git add src/ZeroAlloc.Validation/Attributes/ValidationAttribute.cs
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs
git add tests/ZeroAlloc.Validation.Tests/Attributes/AttributeDeclarationTests.cs
git add tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git add tests/ZeroAlloc.Validation.Tests/Integration/ConditionalModel.cs
git add tests/ZeroAlloc.Validation.Tests/Integration/ConditionalTests.cs
git commit -m "feat: add When/Unless conditional named params to ValidationAttribute"
```

---

## Task 3: Message placeholders + tests

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/PlaceholderModel.cs`
- Create: `tests/ZeroAlloc.Validation.Tests/Integration/PlaceholderTests.cs`

---

### Step 1: Write failing generator tests

Add to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_Placeholder_PropertyName_Replaced()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Item { [NotEmpty(Message = "'{PropertyName}' is required")] public string Name { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    // {PropertyName} replaced with "Name", so the emitted string literal is "'Name' is required"
    Assert.Contains("'Name' is required", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("{PropertyName}", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_Placeholder_ComparisonValue_Replaced()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Item { [GreaterThan(18, Message = "Must be > {ComparisonValue}")] public int Age { get; set; } }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("Must be > 18", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("{ComparisonValue}", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_Placeholder_FromTo_Replaced()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Item { [ExclusiveBetween(0, 100, Message = "Between {From} and {To}")] public double Value { get; set; } }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("Between 0 and 100", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("{From}", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("{To}", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_Placeholder_MinMaxLength_Replaced()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Item { [Length(2, 50, Message = "Length {MinLength}–{MaxLength}")] public string Name { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("Length 2\u201350", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("{MinLength}", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("{MaxLength}", generated, StringComparison.Ordinal);
}
```

### Step 2: Run — verify they fail

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "Generator_Placeholder" -v normal
```

Expected: all FAIL (tokens remain un-replaced).

### Step 3: Add `ResolveMessage` to `RuleEmitter.cs`

**3a. Add a new private method** `ResolveMessage` that replaces the existing `GetMessage` usage:

```csharp
private static string? ResolveMessage(AttributeData attr, string fqn, string propName)
{
    var raw = GetMessage(attr);
    if (raw is null) return null;

    var result = raw.Replace("{PropertyName}", propName, StringComparison.Ordinal);

    // {ComparisonValue} — single numeric arg used by comparison validators
    if (fqn is GreaterThanFqn or LessThanFqn or GreaterThanOrEqualToFqn or LessThanOrEqualToFqn
             or EqualFqn or NotEqualFqn)
    {
        var val = fqn is EqualFqn or NotEqualFqn && IsStringArg(attr, 0)
            ? GetStringArg(attr, 0)
            : GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture);
        result = result.Replace("{ComparisonValue}", val, StringComparison.Ordinal);
    }

    // {MinLength} / {MaxLength}
    if (fqn is LengthFqn)
    {
        result = result
            .Replace("{MinLength}", GetIntArg(attr, 0).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{MaxLength}", GetIntArg(attr, 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
    if (fqn is MinLengthFqn)
        result = result.Replace("{MinLength}", GetIntArg(attr, 0).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    if (fqn is MaxLengthFqn)
        result = result.Replace("{MaxLength}", GetIntArg(attr, 0).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    // {From} / {To}
    if (fqn is ExclusiveBetweenFqn or InclusiveBetweenFqn)
    {
        result = result
            .Replace("{From}", GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{To}",   GetDoubleArg(attr, 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    return result;
}
```

**3b. Replace both call sites** of `GetMessage(attr) ?? GetDefaultMessage(fqn, attr, propName)`:

In `EmitPropertyRulesWithAdd` (line ~114):
```csharp
var message = ResolveMessage(attr, fqn, propName) ?? GetDefaultMessage(fqn, attr, propName);
```

In `EmitFlatPath` loop (line ~207):
```csharp
var message = ResolveMessage(attr, fqn, propName) ?? GetDefaultMessage(fqn, attr, propName);
```

> `ResolveMessage` returns `null` when `Message` is not set, so the fallback to `GetDefaultMessage` is preserved.

### Step 4: Run generator tests — verify they pass

```
dotnet test tests/ZeroAlloc.Validation.Tests --filter "Generator_Placeholder" -v normal
```

Expected: all 4 PASS.

### Step 5: Write failing integration tests

Create `tests/ZeroAlloc.Validation.Tests/Integration/PlaceholderModel.cs`:

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public partial class PlaceholderModel
{
    [NotEmpty(Message = "'{PropertyName}' is required")]
    public string Name { get; set; } = "";

    [GreaterThan(0, Message = "'{PropertyName}' must be greater than {ComparisonValue}")]
    public int Age { get; set; }

    [Length(2, 50, Message = "'{PropertyName}' must be {MinLength}–{MaxLength} chars")]
    public string Bio { get; set; } = "";

    [ExclusiveBetween(0, 100, Message = "'{PropertyName}' must be between {From} and {To}")]
    public double Score { get; set; }
}
```

Create `tests/ZeroAlloc.Validation.Tests/Integration/PlaceholderTests.cs`:

```csharp
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class PlaceholderTests
{
    private readonly PlaceholderModelValidator _validator = new();

    [Fact]
    public void Placeholder_PropertyName_AppearsInMessage()
    {
        var result = _validator.Validate(new PlaceholderModel { Name = "", Age = 1, Bio = "ok", Score = 50 });
        ValidationAssert.HasErrorWithMessage(result, "Name", "'Name' is required");
    }

    [Fact]
    public void Placeholder_ComparisonValue_AppearsInMessage()
    {
        var result = _validator.Validate(new PlaceholderModel { Name = "x", Age = 0, Bio = "ok", Score = 50 });
        ValidationAssert.HasErrorWithMessage(result, "Age", "'Age' must be greater than 0");
    }

    [Fact]
    public void Placeholder_MinMaxLength_AppearsInMessage()
    {
        var result = _validator.Validate(new PlaceholderModel { Name = "x", Age = 1, Bio = "x", Score = 50 });
        ValidationAssert.HasErrorWithMessage(result, "Bio", "'Bio' must be 2\u201350 chars");
    }

    [Fact]
    public void Placeholder_FromTo_AppearsInMessage()
    {
        var result = _validator.Validate(new PlaceholderModel { Name = "x", Age = 1, Bio = "ok", Score = 0 });
        ValidationAssert.HasErrorWithMessage(result, "Score", "'Score' must be between 0 and 100");
    }

    [Fact]
    public void Placeholder_ValidModel_NoErrors()
    {
        ValidationAssert.NoErrors(_validator.Validate(new PlaceholderModel
        {
            Name = "Alice",
            Age = 25,
            Bio = "Developer",
            Score = 50
        }));
    }
}
```

### Step 6: Build and run integration tests

```
dotnet build
dotnet test tests/ZeroAlloc.Validation.Tests --filter "PlaceholderTests" -v normal
```

Expected: all 5 PASS.

### Step 7: Check `ValidationAssert.HasErrorWithMessage` exists

Look at `src/ZeroAlloc.Validation.Testing/ValidationAssert.cs`. If `HasErrorWithMessage` does not exist, add it:

```csharp
public static void HasErrorWithMessage(ValidationResult result, string propertyName, string expectedMessage)
{
    foreach (ref readonly var f in result.Failures)
        if (string.Equals(f.PropertyName, propertyName, StringComparison.Ordinal)
         && string.Equals(f.ErrorMessage, expectedMessage, StringComparison.Ordinal))
            return;
    throw new ValidationAssertException(
        $"Expected a failure for '{propertyName}' with message '{expectedMessage}' but none found.\n" +
        $"Actual failures: {FailureSummary(result)}");
}
```

If `FailureSummary` doesn't exist either, add a private helper:

```csharp
private static string FailureSummary(ValidationResult result)
{
    var sb = new System.Text.StringBuilder();
    foreach (ref readonly var f in result.Failures)
        sb.Append($"\n  [{f.PropertyName}] {f.ErrorMessage}");
    return sb.Length == 0 ? "(none)" : sb.ToString();
}
```

### Step 8: Run all tests

```
dotnet test
```

Expected: all pass, 0 warnings.

### Step 9: Commit

```
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs
git add src/ZeroAlloc.Validation.Testing/ValidationAssert.cs
git add tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git add tests/ZeroAlloc.Validation.Tests/Integration/PlaceholderModel.cs
git add tests/ZeroAlloc.Validation.Tests/Integration/PlaceholderTests.cs
git commit -m "feat: add gen-time message placeholder substitution ({PropertyName}, {ComparisonValue}, etc.)"
```
