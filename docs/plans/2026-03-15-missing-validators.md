# Missing Built-in Validators Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add the 11 missing built-in validation attributes (`Null`, `Empty`, `Equal`, `NotEqual`, `GreaterThanOrEqualTo`, `LessThanOrEqualTo`, `ExclusiveBetween`, `Length`, `IsInEnum`, `IsEnumName`, `PrecisionScale`) to complete the ZeroAlloc.Validation attribute set.

**Architecture:** Each new validator is a C# attribute class in `src/ZeroAlloc.Validation/Attributes/` (inheriting `ValidationAttribute`) plus a FQN constant, `IsRuleAttribute` entry, `BuildCondition` case, and `GetDefaultMessage` case in `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`. `IsInEnum` requires extending `BuildCondition` to accept the property's full type name. `PrecisionScale` requires a new `DecimalValidator` internal helper. Everything else follows the existing pattern exactly.

**Tech Stack:** C# 13, Roslyn `IIncrementalGenerator`, `IPropertySymbol`, `SpecialType`, `SymbolDisplayFormat.FullyQualifiedFormat`, xUnit 2.9.3.

---

## Reference

Design doc: `docs/plans/2026-03-15-missing-validators-design.md`

Key existing files — read these before starting:
- `src/ZeroAlloc.Validation/Attributes/GreaterThanAttribute.cs` — attribute pattern (primary constructor)
- `src/ZeroAlloc.Validation/Attributes/InclusiveBetweenAttribute.cs` — two-arg attribute pattern
- `src/ZeroAlloc.Validation/Attributes/ValidationAttribute.cs` — base class
- `src/ZeroAlloc.Validation/Internal/EmailValidator.cs` — internal helper pattern
- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` — all generator logic lives here
- `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs` — generator test pattern
- `tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs` — integration test pattern

---

### Task 1: `[Null]` and `[Empty]`

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/NullAttribute.cs`
- Create: `src/ZeroAlloc.Validation/Attributes/EmptyAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write two failing generator tests**

Add to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_EmitsNull_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [Null] public string? Name { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("is not null", generated);
    Assert.Contains("\"Name\"", generated);
}

[Fact]
public void Generator_EmitsEmpty_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [Empty] public string? Name { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("IsNullOrEmpty", generated);
    Assert.Contains("\"Name\"", generated);
}
```

**Step 2: Run to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsNull_Check|Generator_EmitsEmpty_Check"
```

Expected: FAIL — `NullAttribute` and `EmptyAttribute` don't exist yet.

**Step 3: Create the attribute classes**

`src/ZeroAlloc.Validation/Attributes/NullAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class NullAttribute : ValidationAttribute { }
```

`src/ZeroAlloc.Validation/Attributes/EmptyAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class EmptyAttribute : ValidationAttribute { }
```

**Step 4: Update `RuleEmitter.cs`**

Add two FQN constants after the existing ones (after `MatchesFqn`):
```csharp
private const string NullFqn  = "ZeroAlloc.Validation.NullAttribute";
private const string EmptyFqn = "ZeroAlloc.Validation.EmptyAttribute";
```

Update `IsRuleAttribute` — extend the `or` chain:
```csharp
return fqn is NotNullFqn or NotEmptyFqn or MinLengthFqn or MaxLengthFqn
    or GreaterThanFqn or LessThanFqn or InclusiveBetweenFqn
    or EmailAddressFqn or MatchesFqn
    or NullFqn or EmptyFqn;
```

Add two cases to `BuildCondition` switch (before the `_` default):
```csharp
NullFqn  => $"{access} is not null",
EmptyFqn => $"!string.IsNullOrEmpty({access})",
```

Add two cases to `GetDefaultMessage` switch:
```csharp
NullFqn  => $"{propName} must be null.",
EmptyFqn => $"{propName} must be empty.",
```

**Step 5: Run to verify tests pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsNull_Check|Generator_EmitsEmpty_Check"
```

Expected: PASS.

**Step 6: Run full suite to confirm no regressions**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

Expected: All tests pass.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Validation/Attributes/NullAttribute.cs src/ZeroAlloc.Validation/Attributes/EmptyAttribute.cs src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add [Null] and [Empty] validation attributes"
```

---

### Task 2: `[Equal]` and `[NotEqual]`

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/EqualAttribute.cs`
- Create: `src/ZeroAlloc.Validation/Attributes/NotEqualAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write failing generator tests**

Add to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_EmitsEqual_Numeric_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [Equal(42.0)] public int Value { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("!= 42", generated);
}

[Fact]
public void Generator_EmitsEqual_String_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [Equal("active")] public string Status { get; set; } = ""; }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("!= \"active\"", generated);
}

[Fact]
public void Generator_EmitsNotEqual_Numeric_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [NotEqual(0.0)] public double Score { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("== 0", generated);
}
```

**Step 2: Run to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsEqual|Generator_EmitsNotEqual"
```

Expected: FAIL.

**Step 3: Create the attribute classes**

`Equal` needs two constructor overloads — cannot use primary constructor syntax:

`src/ZeroAlloc.Validation/Attributes/EqualAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class EqualAttribute : ValidationAttribute
{
    public EqualAttribute(double value) { NumericValue = value; }
    public EqualAttribute(string value) { StringValue = value; }
    public double NumericValue { get; }
    public string? StringValue { get; }
}
```

`src/ZeroAlloc.Validation/Attributes/NotEqualAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class NotEqualAttribute : ValidationAttribute
{
    public NotEqualAttribute(double value) { NumericValue = value; }
    public NotEqualAttribute(string value) { StringValue = value; }
    public double NumericValue { get; }
    public string? StringValue { get; }
}
```

**Step 4: Update `RuleEmitter.cs`**

Add FQN constants:
```csharp
private const string EqualFqn    = "ZeroAlloc.Validation.EqualAttribute";
private const string NotEqualFqn = "ZeroAlloc.Validation.NotEqualAttribute";
```

Update `IsRuleAttribute` — extend the `or` chain to include `EqualFqn or NotEqualFqn`.

Add a private helper to distinguish string vs numeric constructor arg (add after `GetStringArg`):
```csharp
private static bool IsStringArg(AttributeData attr, int index)
{
    if (attr.ConstructorArguments.Length <= index) return false;
    return attr.ConstructorArguments[index].Type?.SpecialType == SpecialType.System_String;
}
```

Add cases to `BuildCondition`:
```csharp
EqualFqn => IsStringArg(attr, 0)
    ? $"{access} != \"{EscapeString(GetStringArg(attr, 0))}\""
    : $"System.Convert.ToDouble({access}) != {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
NotEqualFqn => IsStringArg(attr, 0)
    ? $"{access} == \"{EscapeString(GetStringArg(attr, 0))}\""
    : $"System.Convert.ToDouble({access}) == {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
```

Add cases to `GetDefaultMessage`:
```csharp
EqualFqn    => IsStringArg(attr, 0)
    ? $"{propName} must equal \"{GetStringArg(attr, 0)}\"."
    : $"{propName} must equal {GetArg(attr, 0)}.",
NotEqualFqn => IsStringArg(attr, 0)
    ? $"{propName} must not equal \"{GetStringArg(attr, 0)}\"."
    : $"{propName} must not equal {GetArg(attr, 0)}.",
```

**Step 5: Run to verify tests pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsEqual|Generator_EmitsNotEqual"
```

Expected: PASS.

**Step 6: Run full suite**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

Expected: All pass.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Validation/Attributes/EqualAttribute.cs src/ZeroAlloc.Validation/Attributes/NotEqualAttribute.cs src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add [Equal] and [NotEqual] validation attributes"
```

---

### Task 3: `[GreaterThanOrEqualTo]` and `[LessThanOrEqualTo]`

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/GreaterThanOrEqualToAttribute.cs`
- Create: `src/ZeroAlloc.Validation/Attributes/LessThanOrEqualToAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write failing generator tests**

```csharp
[Fact]
public void Generator_EmitsGreaterThanOrEqualTo_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [GreaterThanOrEqualTo(0)] public int Age { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("< 0", generated);
}

[Fact]
public void Generator_EmitsLessThanOrEqualTo_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [LessThanOrEqualTo(100)] public int Score { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("> 100", generated);
}
```

**Step 2: Run to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsGreaterThanOrEqualTo|Generator_EmitsLessThanOrEqualTo"
```

Expected: FAIL.

**Step 3: Create the attribute classes**

`src/ZeroAlloc.Validation/Attributes/GreaterThanOrEqualToAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class GreaterThanOrEqualToAttribute(double value) : ValidationAttribute
{
    public double Value { get; } = value;
}
```

`src/ZeroAlloc.Validation/Attributes/LessThanOrEqualToAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class LessThanOrEqualToAttribute(double value) : ValidationAttribute
{
    public double Value { get; } = value;
}
```

**Step 4: Update `RuleEmitter.cs`**

Add FQN constants:
```csharp
private const string GreaterThanOrEqualToFqn = "ZeroAlloc.Validation.GreaterThanOrEqualToAttribute";
private const string LessThanOrEqualToFqn    = "ZeroAlloc.Validation.LessThanOrEqualToAttribute";
```

Update `IsRuleAttribute` — extend the `or` chain.

Add cases to `BuildCondition`:
```csharp
GreaterThanOrEqualToFqn => $"System.Convert.ToDouble({access}) < {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
LessThanOrEqualToFqn    => $"System.Convert.ToDouble({access}) > {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
```

Add cases to `GetDefaultMessage`:
```csharp
GreaterThanOrEqualToFqn => $"{propName} must be greater than or equal to {GetArg(attr, 0)}.",
LessThanOrEqualToFqn    => $"{propName} must be less than or equal to {GetArg(attr, 0)}.",
```

**Step 5: Run to verify tests pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsGreaterThanOrEqualTo|Generator_EmitsLessThanOrEqualTo"
```

Expected: PASS.

**Step 6: Run full suite**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

Expected: All pass.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Validation/Attributes/GreaterThanOrEqualToAttribute.cs src/ZeroAlloc.Validation/Attributes/LessThanOrEqualToAttribute.cs src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add [GreaterThanOrEqualTo] and [LessThanOrEqualTo] validation attributes"
```

---

### Task 4: `[ExclusiveBetween]`

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/ExclusiveBetweenAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write failing generator test**

```csharp
[Fact]
public void Generator_EmitsExclusiveBetween_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [ExclusiveBetween(0, 100)] public int Value { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("<= 0", generated);
    Assert.Contains(">= 100", generated);
}
```

**Step 2: Run to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsExclusiveBetween"
```

Expected: FAIL.

**Step 3: Create the attribute class**

`src/ZeroAlloc.Validation/Attributes/ExclusiveBetweenAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class ExclusiveBetweenAttribute(double min, double max) : ValidationAttribute
{
    public double Min { get; } = min;
    public double Max { get; } = max;
}
```

**Step 4: Update `RuleEmitter.cs`**

Add FQN constant:
```csharp
private const string ExclusiveBetweenFqn = "ZeroAlloc.Validation.ExclusiveBetweenAttribute";
```

Update `IsRuleAttribute`.

Add case to `BuildCondition`:
```csharp
ExclusiveBetweenFqn => $"System.Convert.ToDouble({access}) <= {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)} || System.Convert.ToDouble({access}) >= {GetDoubleArg(attr, 1).ToString(CultureInfo.InvariantCulture)}",
```

Add case to `GetDefaultMessage`:
```csharp
ExclusiveBetweenFqn => $"{propName} must be exclusively between {GetArg(attr, 0)} and {GetArg(attr, 1)}.",
```

**Step 5: Run to verify test passes**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsExclusiveBetween"
```

Expected: PASS.

**Step 6: Run full suite**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Validation/Attributes/ExclusiveBetweenAttribute.cs src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add [ExclusiveBetween] validation attribute"
```

---

### Task 5: `[Length]`

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/LengthAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write failing generator test**

```csharp
[Fact]
public void Generator_EmitsLength_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [Length(2, 50)] public string Name { get; set; } = ""; }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains(".Length < 2", generated);
    Assert.Contains(".Length > 50", generated);
}
```

**Step 2: Run to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsLength_Check"
```

Expected: FAIL.

**Step 3: Create the attribute class**

`src/ZeroAlloc.Validation/Attributes/LengthAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class LengthAttribute(int min, int max) : ValidationAttribute
{
    public int Min { get; } = min;
    public int Max { get; } = max;
}
```

**Step 4: Update `RuleEmitter.cs`**

Add FQN constant:
```csharp
private const string LengthFqn = "ZeroAlloc.Validation.LengthAttribute";
```

Update `IsRuleAttribute`.

Add case to `BuildCondition`:
```csharp
LengthFqn => $"{access}.Length < {GetIntArg(attr, 0)} || {access}.Length > {GetIntArg(attr, 1)}",
```

Add case to `GetDefaultMessage`:
```csharp
LengthFqn => $"{propName} must be between {GetArg(attr, 0)} and {GetArg(attr, 1)} characters.",
```

**Step 5: Run to verify test passes**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsLength_Check"
```

Expected: PASS.

**Step 6: Run full suite**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Validation/Attributes/LengthAttribute.cs src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add [Length] validation attribute"
```

---

### Task 6: `[IsInEnum]`

This task also introduces the `propTypeFullName` parameter to `BuildCondition` and the `GetNullableUnwrappedFullTypeName` helper — both needed only for `IsInEnum`.

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/IsInEnumAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write failing generator test**

```csharp
[Fact]
public void Generator_EmitsIsInEnum_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        public enum Color { Red, Green, Blue }
        [Validate]
        public class Foo { [IsInEnum] public Color Hue { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("IsDefined", generated);
    Assert.Contains("Color", generated);
}
```

**Step 2: Run to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsIsInEnum_Check"
```

Expected: FAIL.

**Step 3: Create the attribute class**

`src/ZeroAlloc.Validation/Attributes/IsInEnumAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class IsInEnumAttribute : ValidationAttribute { }
```

**Step 4: Update `RuleEmitter.cs`**

Add FQN constant:
```csharp
private const string IsInEnumFqn = "ZeroAlloc.Validation.IsInEnumAttribute";
```

Update `IsRuleAttribute`.

**Extend `BuildCondition` signature** to accept the property type full name — change the signature and all call sites:

```csharp
// Old signature:
private static string BuildCondition(string fqn, AttributeData attr, string access) =>

// New signature:
private static string BuildCondition(string fqn, AttributeData attr, string access, string propTypeFullName = "") =>
```

Add case to `BuildCondition`:
```csharp
IsInEnumFqn => $"!global::System.Enum.IsDefined(typeof({propTypeFullName}), {access})",
```

Add case to `GetDefaultMessage`:
```csharp
IsInEnumFqn => $"{propName} is not a valid value.",
```

**Add `GetNullableUnwrappedFullTypeName` helper** (add after `GetNestedValidateProperties`):
```csharp
private static string GetNullableUnwrappedFullTypeName(IPropertySymbol prop)
{
    var type = prop.Type;
    if (type is INamedTypeSymbol named && named.IsGenericType
        && named.OriginalDefinition.ToDisplayString() == "System.Nullable<T>")
        type = named.TypeArguments[0];
    return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
```

**Update both call sites of `BuildCondition`** in `EmitValidateBody` (there are two — one in the `hasNested` branch and one in the flat branch). Both currently look like:
```csharp
var condition = BuildCondition(fqn, attr, propAccess);
```
Change both to:
```csharp
var propTypeFullName = GetNullableUnwrappedFullTypeName(prop);
var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName);
```

**Step 5: Run to verify test passes**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsIsInEnum_Check"
```

Expected: PASS.

**Step 6: Run full suite**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

Expected: All pass.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Validation/Attributes/IsInEnumAttribute.cs src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add [IsInEnum] validation attribute with prop-type forwarding in BuildCondition"
```

---

### Task 7: `[IsEnumName]`

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/IsEnumNameAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write failing generator test**

```csharp
[Fact]
public void Generator_EmitsIsEnumName_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        public enum Color { Red, Green, Blue }
        [Validate]
        public class Foo { [IsEnumName(typeof(Color))] public string ColorName { get; set; } = ""; }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("IsDefined", generated);
    Assert.Contains("Color", generated);
}
```

**Step 2: Run to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsIsEnumName_Check"
```

Expected: FAIL.

**Step 3: Create the attribute class**

`src/ZeroAlloc.Validation/Attributes/IsEnumNameAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class IsEnumNameAttribute(Type enumType) : ValidationAttribute
{
    public Type EnumType { get; } = enumType;
}
```

**Step 4: Update `RuleEmitter.cs`**

Add FQN constant:
```csharp
private const string IsEnumNameFqn = "ZeroAlloc.Validation.IsEnumNameAttribute";
```

Update `IsRuleAttribute`.

**Add `GetTypeArgFullName` helper** (add after `IsStringArg`):
```csharp
private static string GetTypeArgFullName(AttributeData attr, int index)
{
    if (attr.ConstructorArguments.Length <= index) return "global::System.Enum";
    var typeSymbol = attr.ConstructorArguments[index].Value as ITypeSymbol;
    return typeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Enum";
}
```

Add case to `BuildCondition`:
```csharp
IsEnumNameFqn => $"!global::System.Enum.IsDefined(typeof({GetTypeArgFullName(attr, 0)}), {access})",
```

Add case to `GetDefaultMessage`:
```csharp
IsEnumNameFqn => $"{propName} is not a valid enum name.",
```

**Step 5: Run to verify test passes**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsIsEnumName_Check"
```

Expected: PASS.

**Step 6: Run full suite**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Validation/Attributes/IsEnumNameAttribute.cs src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add [IsEnumName] validation attribute"
```

---

### Task 8: `[PrecisionScale]` + `DecimalValidator`

**Files:**
- Create: `src/ZeroAlloc.Validation/Internal/DecimalValidator.cs`
- Create: `src/ZeroAlloc.Validation/Attributes/PrecisionScaleAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write failing generator test**

```csharp
[Fact]
public void Generator_EmitsPrecisionScale_Check()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class Foo { [PrecisionScale(5, 2)] public decimal Amount { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("ExceedsPrecisionScale", generated);
    Assert.Contains("5", generated);
    Assert.Contains("2", generated);
}
```

**Step 2: Run to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsPrecisionScale_Check"
```

Expected: FAIL.

**Step 3: Create the `DecimalValidator` helper**

`src/ZeroAlloc.Validation/Internal/DecimalValidator.cs`:
```csharp
namespace ZeroAlloc.Validation.Internal;

internal static class DecimalValidator
{
    internal static bool ExceedsPrecisionScale(decimal value, int precision, int scale)
    {
        // Extract scale (number of decimal places) from decimal bits — zero allocation
        var bits = decimal.GetBits(value);
        int actualScale = (bits[3] >> 16) & 0x1F;
        if (actualScale > scale) return true;

        // Count integer digits
        var abs = decimal.Truncate(decimal.Abs(value));
        int intDigits = abs == 0m ? 0 : (int)System.Math.Floor(System.Math.Log10((double)abs)) + 1;
        return intDigits + scale > precision;
    }
}
```

**Step 4: Create the attribute class**

`src/ZeroAlloc.Validation/Attributes/PrecisionScaleAttribute.cs`:
```csharp
namespace ZeroAlloc.Validation;

public sealed class PrecisionScaleAttribute(int precision, int scale) : ValidationAttribute
{
    public int Precision { get; } = precision;
    public int Scale { get; } = scale;
}
```

**Step 5: Update `RuleEmitter.cs`**

Add FQN constant:
```csharp
private const string PrecisionScaleFqn = "ZeroAlloc.Validation.PrecisionScaleAttribute";
```

Update `IsRuleAttribute`.

Add case to `BuildCondition`:
```csharp
PrecisionScaleFqn => $"global::ZeroAlloc.Validation.Internal.DecimalValidator.ExceedsPrecisionScale({access}, {GetIntArg(attr, 0)}, {GetIntArg(attr, 1)})",
```

Add case to `GetDefaultMessage`:
```csharp
PrecisionScaleFqn => $"{propName} must not exceed {GetArg(attr, 0)} digits total with {GetArg(attr, 1)} decimal places.",
```

**Step 6: Run to verify test passes**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "Generator_EmitsPrecisionScale_Check"
```

Expected: PASS.

**Step 7: Run full suite**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

**Step 8: Commit**

```bash
git add src/ZeroAlloc.Validation/Internal/DecimalValidator.cs src/ZeroAlloc.Validation/Attributes/PrecisionScaleAttribute.cs src/ZeroAlloc.Validation.Generator/RuleEmitter.cs tests/ZeroAlloc.Validation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add [PrecisionScale] validation attribute and DecimalValidator helper"
```

---

### Task 9: End-to-end integration tests

**Files:**
- Modify: `tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs`

Append the following model classes and test classes at the end of the file.

**Step 1: Add models and tests**

```csharp
// ── Null / Empty ────────────────────────────────────────────────────────────

[Validate]
public class NullEmptyModel
{
    [Null]
    public string? MustBeNull { get; set; }

    [Empty(Message = "Must be empty.")]
    public string MustBeEmpty { get; set; } = "";
}

public class NullEmptyTests
{
    private readonly NullEmptyModelValidator _validator = new();

    [Fact]
    public void Null_WhenNull_Passes()
    {
        var result = _validator.Validate(new NullEmptyModel { MustBeNull = null, MustBeEmpty = "" });
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Null_WhenNotNull_Fails()
    {
        var result = _validator.Validate(new NullEmptyModel { MustBeNull = "oops", MustBeEmpty = "" });
        ValidationAssert.HasError(result, "MustBeNull");
    }

    [Fact]
    public void Empty_WhenNotEmpty_FailsWithCustomMessage()
    {
        var result = _validator.Validate(new NullEmptyModel { MustBeNull = null, MustBeEmpty = "x" });
        ValidationAssert.HasError(result, "MustBeEmpty");
        Assert.Equal("Must be empty.", result.Failures.ToArray().First(f => f.PropertyName == "MustBeEmpty").ErrorMessage);
    }
}

// ── Equal / NotEqual ─────────────────────────────────────────────────────────

[Validate]
public class EqualityModel
{
    [Equal("active", Message = "Status must be active.")]
    public string Status { get; set; } = "";

    [NotEqual(0.0)]
    public double Score { get; set; }
}

public class EqualityTests
{
    private readonly EqualityModelValidator _validator = new();

    [Fact]
    public void Valid_EqualityModel_Passes()
    {
        var result = _validator.Validate(new EqualityModel { Status = "active", Score = 1.0 });
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Equal_WrongString_Fails()
    {
        var result = _validator.Validate(new EqualityModel { Status = "inactive", Score = 1.0 });
        ValidationAssert.HasError(result, "Status");
        Assert.Equal("Status must be active.", result.Failures.ToArray().First(f => f.PropertyName == "Status").ErrorMessage);
    }

    [Fact]
    public void NotEqual_MatchingValue_Fails()
    {
        var result = _validator.Validate(new EqualityModel { Status = "active", Score = 0.0 });
        ValidationAssert.HasError(result, "Score");
    }
}

// ── GreaterThanOrEqualTo / LessThanOrEqualTo ─────────────────────────────────

[Validate]
public class RangeModel
{
    [GreaterThanOrEqualTo(0)]
    [LessThanOrEqualTo(100)]
    public int Percentage { get; set; }
}

public class RangeTests
{
    private readonly RangeModelValidator _validator = new();

    [Fact]
    public void Valid_Boundary_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new RangeModel { Percentage = 0 }));
        ValidationAssert.NoErrors(_validator.Validate(new RangeModel { Percentage = 100 }));
    }

    [Fact]
    public void BelowMinimum_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new RangeModel { Percentage = -1 }), "Percentage");
    }

    [Fact]
    public void AboveMaximum_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new RangeModel { Percentage = 101 }), "Percentage");
    }
}

// ── ExclusiveBetween ──────────────────────────────────────────────────────────

[Validate]
public class ExclusiveBetweenModel
{
    [ExclusiveBetween(0, 100)]
    public int Value { get; set; }
}

public class ExclusiveBetweenTests
{
    private readonly ExclusiveBetweenModelValidator _validator = new();

    [Fact]
    public void Middle_Value_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new ExclusiveBetweenModel { Value = 50 }));
    }

    [Fact]
    public void Boundary_Value_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new ExclusiveBetweenModel { Value = 0 }), "Value");
        ValidationAssert.HasError(_validator.Validate(new ExclusiveBetweenModel { Value = 100 }), "Value");
    }
}

// ── Length ────────────────────────────────────────────────────────────────────

[Validate]
public class LengthModel
{
    [Length(2, 10)]
    public string Name { get; set; } = "";
}

public class LengthTests
{
    private readonly LengthModelValidator _validator = new();

    [Fact]
    public void Valid_Length_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new LengthModel { Name = "Hi" }));
        ValidationAssert.NoErrors(_validator.Validate(new LengthModel { Name = "1234567890" }));
    }

    [Fact]
    public void TooShort_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new LengthModel { Name = "A" }), "Name");
    }

    [Fact]
    public void TooLong_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new LengthModel { Name = "12345678901" }), "Name");
    }
}

// ── IsInEnum ──────────────────────────────────────────────────────────────────

public enum TrafficLight { Red, Yellow, Green }

[Validate]
public class EnumModel
{
    [IsInEnum]
    public TrafficLight Light { get; set; }
}

public class IsInEnumTests
{
    private readonly EnumModelValidator _validator = new();

    [Fact]
    public void DefinedValue_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new EnumModel { Light = TrafficLight.Green }));
    }

    [Fact]
    public void UndefinedValue_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new EnumModel { Light = (TrafficLight)99 }), "Light");
    }
}

// ── IsEnumName ────────────────────────────────────────────────────────────────

[Validate]
public class EnumNameModel
{
    [IsEnumName(typeof(TrafficLight))]
    public string LightName { get; set; } = "";
}

public class IsEnumNameTests
{
    private readonly EnumNameModelValidator _validator = new();

    [Fact]
    public void ValidName_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new EnumNameModel { LightName = "Red" }));
    }

    [Fact]
    public void InvalidName_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new EnumNameModel { LightName = "Purple" }), "LightName");
    }
}

// ── PrecisionScale ────────────────────────────────────────────────────────────

[Validate]
public class DecimalModel
{
    [PrecisionScale(5, 2, Message = "Amount precision exceeded.")]
    public decimal Amount { get; set; }
}

public class PrecisionScaleTests
{
    private readonly DecimalModelValidator _validator = new();

    [Fact]
    public void ValidDecimal_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new DecimalModel { Amount = 123.45m }));
    }

    [Fact]
    public void TooManyDecimalPlaces_Fails()
    {
        var result = _validator.Validate(new DecimalModel { Amount = 1.999m });
        ValidationAssert.HasError(result, "Amount");
        Assert.Equal("Amount precision exceeded.", result.Failures.ToArray().First(f => f.PropertyName == "Amount").ErrorMessage);
    }

    [Fact]
    public void TooManyTotalDigits_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new DecimalModel { Amount = 1234.00m }), "Amount");
    }
}
```

**Step 2: Build to confirm all validators and test classes compile**

```bash
dotnet build tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
```

Expected: 0 errors.

**Step 3: Run all integration tests**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj --filter "NullEmptyTests|EqualityTests|RangeTests|ExclusiveBetweenTests|LengthTests|IsInEnumTests|IsEnumNameTests|PrecisionScaleTests"
```

Expected: All pass.

**Step 4: Run full suite**

```bash
dotnet test ZeroAlloc.Validation.slnx
```

Expected: All tests pass across net8.0, net9.0, net10.0.

**Step 5: Commit**

```bash
git add tests/ZeroAlloc.Validation.Tests/Integration/EndToEndTests.cs
git commit -m "test: add end-to-end integration tests for all new validators"
```

---

### Task 10: Final verification

**Step 1: Full build**

```bash
dotnet build ZeroAlloc.Validation.slnx
```

Expected: 0 errors, 0 warnings.

**Step 2: Full test run**

```bash
dotnet test ZeroAlloc.Validation.slnx
```

Expected: All tests pass.

**Step 3: Final commit**

```bash
git commit --allow-empty -m "chore: missing validators complete"
```
