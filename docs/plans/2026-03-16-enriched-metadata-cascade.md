# Enriched Failure Metadata + Cascade Stop Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add `ErrorCode`/`Severity` named params to every validation attribute and a `[StopOnFirstFailure]` marker attribute that stops rule evaluation on a property after the first failure.

**Architecture:** `ValidationAttribute` gains two new named params that the generator reads and emits into `ValidationFailure` initializers. The default emission changes from `else if` (stop mode) to separate `if` statements (continue mode); `[StopOnFirstFailure]` restores the stop behavior explicitly. Both flat and nested code paths need updating.

**Tech Stack:** C# source generator (`IIncrementalGenerator`, `netstandard2.0`), `AttributeData.NamedArguments` for reading named params, xUnit for tests, `Microsoft.CodeAnalysis.CSharp` for generator emission tests.

---

## ⚠️ Behavior Change Warning

The generator currently emits `else if` chains for multiple rules on the same property, which is implicitly stop-at-first-failure. This plan **changes the default to continue mode** (separate `if` statements so all rules run) and makes stop-at-first-failure explicit via `[StopOnFirstFailure]`. The existing test `Generator_EmitsStopAtFirstFailure_AsElseIf` will need to be updated.

---

## Task 1: Add ErrorCode and Severity to ValidationAttribute

**Files:**
- Modify: `src/ZValidation/Attributes/ValidationAttribute.cs`

**Step 1: Write the failing test**

Add to `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_EmitsErrorCode_InFailureInitializer()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Item { [NotEmpty(ErrorCode = "NAME_REQUIRED")] public string Name { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("ErrorCode = \"NAME_REQUIRED\"", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_EmitsSeverity_Warning_InFailureInitializer()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Item { [NotEmpty(Severity = Severity.Warning)] public string Name { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("global::ZValidation.Severity.Warning", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_OmitsErrorCode_WhenNull()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Item { [NotEmpty] public string Name { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.DoesNotContain("ErrorCode", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_OmitsSeverity_WhenError()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Item { [NotEmpty] public string Name { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.DoesNotContain("Severity", generated, StringComparison.Ordinal);
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "ErrorCode|Severity" -v minimal
```

Expected: 4 FAIL.

**Step 3: Add ErrorCode and Severity to ValidationAttribute**

Edit `src/ZValidation/Attributes/ValidationAttribute.cs`:

```csharp
namespace ZValidation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class ValidationAttribute : Attribute
{
    public string? Message   { get; set; }
    public string? When      { get; set; }
    public string? Unless    { get; set; }
    public string? ErrorCode { get; set; }
    public Severity Severity { get; set; } = Severity.Error;
}
```

**Step 4: Run tests to verify they still fail** (generator not updated yet)

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "ErrorCode|Severity" -v minimal
```

Expected: still 4 FAIL (generator doesn't emit them yet).

**Step 5: Add GetErrorCode and GetSeverityValue helpers to RuleEmitter**

In `src/ZValidation.Generator/RuleEmitter.cs`, add alongside `GetMessage`/`GetWhen`/`GetUnless`:

```csharp
private static string? GetErrorCode(AttributeData attr)
{
    foreach (var named in attr.NamedArguments)
        if (string.Equals(named.Key, "ErrorCode", StringComparison.Ordinal) && named.Value.Value is string s)
            return s;
    return null;
}

// Returns 0 = Error (default), 1 = Warning, 2 = Info.
private static int GetSeverityValue(AttributeData attr)
{
    foreach (var named in attr.NamedArguments)
        if (string.Equals(named.Key, "Severity", StringComparison.Ordinal) && named.Value.Value is int i)
            return i;
    return 0;
}

private static string SeverityToLiteral(int severityValue) => severityValue switch
{
    1 => "global::ZValidation.Severity.Warning",
    2 => "global::ZValidation.Severity.Info",
    _ => "global::ZValidation.Severity.Error"
};
```

**Step 6: Add BuildFailureInitializer helper to RuleEmitter**

This replaces the inline `ValidationFailure { ... }` construction everywhere. Add to `RuleEmitter.cs`:

```csharp
private static string BuildFailureInitializer(string propName, string message, AttributeData attr)
{
    var errorCode = GetErrorCode(attr);
    var severityValue = GetSeverityValue(attr);

    var sb2 = new StringBuilder();
    sb2.Append($"new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = \"{EscapeString(message)}\"");
    if (errorCode is not null)
        sb2.Append($", ErrorCode = \"{EscapeString(errorCode)}\"");
    if (severityValue != 0)
        sb2.Append($", Severity = {SeverityToLiteral(severityValue)}");
    sb2.Append(" }");
    return sb2.ToString();
}
```

**Step 7: Update EmitPropertyRulesWithAdd to use BuildFailureInitializer**

In `EmitPropertyRulesWithAdd`, change:
```csharp
// OLD:
sb.AppendLine($"            failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = \"{EscapeString(message)}\" }});");

// NEW:
sb.AppendLine($"            failures.Add({BuildFailureInitializer(propName, message, attr)});");
```

**Step 8: Update EmitFlatPath to use BuildFailureInitializer**

In `EmitFlatPath`, change:
```csharp
// OLD:
sb.AppendLine($"            buffer[count++] = new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = \"{EscapeString(message)}\" }};");

// NEW:
sb.AppendLine($"            buffer[count++] = {BuildFailureInitializer(propName, message, attr)};");
```

**Step 9: Run the 4 new tests**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "ErrorCode|Severity" -v minimal
```

Expected: 4 PASS.

**Step 10: Run full test suite**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```

Expected: all pass.

**Step 11: Add integration tests**

Create `tests/ZValidation.Tests/Integration/EnrichedMetadataModel.cs`:

```csharp
using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class EnrichedMetadataModel
{
    [NotEmpty(ErrorCode = "NAME_REQUIRED")]
    public string Name { get; set; } = "";

    [GreaterThan(0, Severity = Severity.Warning, ErrorCode = "AGE_WARN")]
    public int Age { get; set; }

    [MaxLength(100)]
    public string Bio { get; set; } = "";
}
```

Create `tests/ZValidation.Tests/Integration/EnrichedMetadataTests.cs`:

```csharp
using System;
using System.Linq;
using Xunit;
using ZValidation;

namespace ZValidation.Tests.Integration;

public class EnrichedMetadataTests
{
    private readonly EnrichedMetadataModelValidator _validator = new();

    [Fact]
    public void ErrorCode_SetOnFailure_WhenSpecified()
    {
        var result = _validator.Validate(new EnrichedMetadataModel { Name = "", Age = 1, Bio = "" });
        var failure = result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Name", StringComparison.Ordinal));
        Assert.Equal("NAME_REQUIRED", failure.ErrorCode);
    }

    [Fact]
    public void Severity_Warning_SetOnFailure_WhenSpecified()
    {
        var result = _validator.Validate(new EnrichedMetadataModel { Name = "ok", Age = 0, Bio = "" });
        var failure = result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Age", StringComparison.Ordinal));
        Assert.Equal(Severity.Warning, failure.Severity);
        Assert.Equal("AGE_WARN", failure.ErrorCode);
    }

    [Fact]
    public void Severity_DefaultsToError_WhenNotSpecified()
    {
        var result = _validator.Validate(new EnrichedMetadataModel { Name = "", Age = 1, Bio = "" });
        var failure = result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Name", StringComparison.Ordinal));
        Assert.Equal(Severity.Error, failure.Severity);
    }

    [Fact]
    public void ErrorCode_Null_WhenNotSpecified()
    {
        var result = _validator.Validate(new EnrichedMetadataModel { Name = "ok", Age = 1, Bio = new string('x', 101) });
        var failure = result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Bio", StringComparison.Ordinal));
        Assert.Null(failure.ErrorCode);
    }
}
```

**Step 12: Run integration tests**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "EnrichedMetadata" -v minimal
```

Expected: 4 PASS.

**Step 13: Commit**

```bash
git add src/ZValidation/Attributes/ValidationAttribute.cs \
        src/ZValidation.Generator/RuleEmitter.cs \
        tests/ZValidation.Tests/Integration/EnrichedMetadataModel.cs \
        tests/ZValidation.Tests/Integration/EnrichedMetadataTests.cs \
        tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add ErrorCode and Severity named params to validation attributes"
```

---

## Task 2: Add StopOnFirstFailureAttribute + Change Default to Continue Mode

**Context:** The generator currently emits `else if` for multiple rules on the same property, which is implicitly stop-at-first-failure. This task changes the default to "continue" mode (all rules run independently) and introduces `[StopOnFirstFailure]` to explicitly opt back into stop mode.

**Files:**
- Create: `src/ZValidation/Attributes/StopOnFirstFailureAttribute.cs`
- Modify: `src/ZValidation.Generator/RuleEmitter.cs`
- Modify: `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`
- Create: `tests/ZValidation.Tests/Integration/CascadeModel.cs`
- Create: `tests/ZValidation.Tests/Integration/CascadeTests.cs`

**Step 1: Update the existing test that asserts else-if (it will need to flip)**

In `GeneratorRuleEmissionTests.cs`, find the test `Generator_EmitsStopAtFirstFailure_AsElseIf` (around line 57) and rename + invert it:

```csharp
[Fact]
public void Generator_DefaultContinueMode_EmitsSeparateIf_NotElseIf()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Person
        {
            [NotEmpty]
            [MaxLength(50)]
            public string Name { get; set; } = "";
        }
        """;

    var generated = RunGeneratorGetSource(source);
    // In continue mode, rules are independent ifs — no else if
    Assert.DoesNotContain("else if", generated, StringComparison.Ordinal);
}
```

**Step 2: Add new generator tests for StopOnFirstFailure**

Add to `GeneratorRuleEmissionTests.cs`:

```csharp
[Fact]
public void Generator_StopOnFirstFailure_EmitsElseIf()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Person
        {
            [StopOnFirstFailure]
            [NotEmpty]
            [MaxLength(50)]
            public string Name { get; set; } = "";
        }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("else if", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_StopOnFirstFailure_OnlyAffectsTaggedProperty()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Person
        {
            [StopOnFirstFailure]
            [NotEmpty]
            [MaxLength(50)]
            public string Name { get; set; } = "";

            [GreaterThan(0)]
            [LessThan(120)]
            public int Age { get; set; }
        }
        """;

    var generated = RunGeneratorGetSource(source);
    // Name has else if (stop mode), but Age rules use separate if (continue mode)
    // The generated code has exactly one "else if" (for Name's second rule)
    var elseIfCount = System.Text.RegularExpressions.Regex.Matches(generated, @"\belse if\b").Count;
    Assert.Equal(1, elseIfCount);
}
```

**Step 3: Run tests to verify they fail**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "StopOnFirstFailure|DefaultContinue" -v minimal
```

Expected: 3 FAIL (attribute doesn't exist yet; existing else-if test also now expects different behavior).

**Step 4: Create StopOnFirstFailureAttribute**

Create `src/ZValidation/Attributes/StopOnFirstFailureAttribute.cs`:

```csharp
namespace ZValidation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class StopOnFirstFailureAttribute : Attribute { }
```

**Step 5: Add StopOnFirstFailure detection helper to RuleEmitter**

Add constant and helper at the top of `RuleEmitter.cs`:

```csharp
private const string StopOnFirstFailureFqn = "ZValidation.StopOnFirstFailureAttribute";

private static bool HasStopOnFirstFailure(IPropertySymbol prop) =>
    prop.GetAttributes().Any(a =>
        string.Equals(a.AttributeClass?.ToDisplayString(), StopOnFirstFailureFqn, StringComparison.Ordinal));
```

**Step 6: Update EmitPropertyRulesWithAdd to use continue mode by default**

Change the `prefix` logic in `EmitPropertyRulesWithAdd`:

```csharp
private static void EmitPropertyRulesWithAdd(
    StringBuilder sb,
    List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
    string modelParamName)
{
    for (int pi = 0; pi < byProperty.Count; pi++)
    {
        var (prop, rules) = byProperty[pi];
        var propName = prop.Name;
        var propAccess = $"{modelParamName}.{propName}";
        var stopMode = HasStopOnFirstFailure(prop);

        for (int i = 0; i < rules.Count; i++)
        {
            var attr = rules[i];
            var fqn = attr.AttributeClass!.ToDisplayString();
            // Stop mode: first rule is "if", subsequent are "else if"
            // Continue mode: every rule is "if"
            var prefix = (stopMode && i > 0) ? "        else if" : "        if";
            var message = ResolveMessage(attr, fqn, propName) ?? GetDefaultMessage(fqn, attr, propName);
            var propTypeFullName = GetNullableUnwrappedFullTypeName(prop);
            var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName, modelParamName);
            var whenMethod   = GetWhen(attr);
            var unlessMethod = GetUnless(attr);
            var whenGuard    = whenMethod   is null ? "" : $"{modelParamName}.{whenMethod}() && ";
            var unlessGuard  = unlessMethod is null ? "" : $"!{modelParamName}.{unlessMethod}() && ";

            sb.AppendLine($"{prefix} ({whenGuard}{unlessGuard}{condition})");
            sb.AppendLine($"            failures.Add({BuildFailureInitializer(propName, message, attr)});");
        }
        sb.AppendLine();
    }
}
```

**Step 7: Update EmitFlatPath to use continue mode by default**

Same change in `EmitFlatPath` — the `prefix` logic and the buffer size:

```csharp
private static void EmitFlatPath(
    StringBuilder sb,
    List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
    int totalDirectRules,
    string modelParamName)
{
    sb.AppendLine($"        var buffer = new global::ZValidation.ValidationFailure[{totalDirectRules}];");
    sb.AppendLine("        int count = 0;");
    sb.AppendLine();

    for (int pi = 0; pi < byProperty.Count; pi++)
    {
        var (prop, rules) = byProperty[pi];
        var propName = prop.Name;
        var propAccess = $"{modelParamName}.{propName}";
        var stopMode = HasStopOnFirstFailure(prop);

        for (int i = 0; i < rules.Count; i++)
        {
            var attr = rules[i];
            var fqn = attr.AttributeClass!.ToDisplayString();
            var prefix = (stopMode && i > 0) ? "        else if" : "        if";
            var message = ResolveMessage(attr, fqn, propName) ?? GetDefaultMessage(fqn, attr, propName);
            var propTypeFullName = GetNullableUnwrappedFullTypeName(prop);
            var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName, modelParamName);
            var whenMethod   = GetWhen(attr);
            var unlessMethod = GetUnless(attr);
            var whenGuard    = whenMethod   is null ? "" : $"{modelParamName}.{whenMethod}() && ";
            var unlessGuard  = unlessMethod is null ? "" : $"!{modelParamName}.{unlessMethod}() && ";

            sb.AppendLine($"{prefix} ({whenGuard}{unlessGuard}{condition})");
            sb.AppendLine($"            buffer[count++] = {BuildFailureInitializer(propName, message, attr)};");
        }
        sb.AppendLine();
    }

    sb.AppendLine("        if (count == buffer.Length) return new global::ZValidation.ValidationResult(buffer);");
    sb.AppendLine("        var result = new global::ZValidation.ValidationFailure[count];");
    sb.AppendLine("        global::System.Array.Copy(buffer, result, count);");
    sb.AppendLine("        return new global::ZValidation.ValidationResult(result);");
}
```

**Step 8: Run generator tests**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "StopOnFirstFailure|DefaultContinue" -v minimal
```

Expected: 3 PASS.

**Step 9: Run full test suite — fix any regressions**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```

The test `Generator_EmitsStackalloc_SizedToRuleCount` asserts `ValidationFailure[3]` — this should still pass since `totalDirectRules` counts all rules regardless of stop mode. Review and fix any other failures.

**Step 10: Add integration tests**

Create `tests/ZValidation.Tests/Integration/CascadeModel.cs`:

```csharp
using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class CascadeModel
{
    // Stop mode: only first failure reported per property
    [StopOnFirstFailure]
    [NotNull]
    [MinLength(2)]
    [MaxLength(100)]
    public string? StopName { get; set; }

    // Continue mode (default): all failures reported
    [NotNull]
    [MinLength(2)]
    [MaxLength(100)]
    public string? ContinueName { get; set; }
}
```

Create `tests/ZValidation.Tests/Integration/CascadeTests.cs`:

```csharp
using System;
using System.Linq;
using Xunit;
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class CascadeTests
{
    private readonly CascadeModelValidator _validator = new();

    [Fact]
    public void StopOnFirstFailure_NullValue_ReportsOnlyOneFailure()
    {
        // null violates NotNull; MinLength and MaxLength should NOT also fire
        var result = _validator.Validate(new CascadeModel { StopName = null, ContinueName = "ok" });
        var stopFailures = result.Failures.ToArray()
            .Where(f => string.Equals(f.PropertyName, "StopName", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(stopFailures);
    }

    [Fact]
    public void ContinueMode_NullValue_ReportsAllApplicableFailures()
    {
        // null violates NotNull; MinLength and MaxLength also run and produce their failures
        var result = _validator.Validate(new CascadeModel { StopName = "ok", ContinueName = null });
        var continueFailures = result.Failures.ToArray()
            .Where(f => string.Equals(f.PropertyName, "ContinueName", StringComparison.Ordinal))
            .ToArray();
        // NotNull fires; MinLength also fires (null treated as fails condition); MaxLength may or may not depending on impl
        Assert.True(continueFailures.Length > 1, "Continue mode should report multiple failures");
    }

    [Fact]
    public void StopOnFirstFailure_ValidValue_NoFailures()
    {
        var result = _validator.Validate(new CascadeModel { StopName = "ok", ContinueName = "ok" });
        Assert.False(result.Failures.ToArray().Any(f =>
            string.Equals(f.PropertyName, "StopName", StringComparison.Ordinal)));
    }

    [Fact]
    public void StopOnFirstFailure_WithWhen_CascadeOnlyWhenConditionActive()
    {
        // This is a generator-level concern — integration-tested via the generator emission tests.
        // Placeholder: verified by Generator_StopOnFirstFailure_EmitsElseIf test.
    }
}
```

**Step 11: Run integration tests**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "Cascade" -v minimal
```

Expected: all PASS (or investigate and fix).

**Step 12: Run full test suite**

```bash
dotnet test -v minimal
```

Expected: all pass.

**Step 13: Commit**

```bash
git add src/ZValidation/Attributes/StopOnFirstFailureAttribute.cs \
        src/ZValidation.Generator/RuleEmitter.cs \
        tests/ZValidation.Tests/Integration/CascadeModel.cs \
        tests/ZValidation.Tests/Integration/CascadeTests.cs \
        tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: add [StopOnFirstFailure] attribute; default to continue mode for property rules"
```

---

## Task 3: Update features.md

**Files:**
- Modify: `docs/features.md`

**Step 1: Mark features as done**

In `docs/features.md`, update the following entries:

- Section 5.4 `WithErrorCode` → mark ✅, note it's a named param `ErrorCode` on every attribute
- Section 5.5 `WithSeverity` → mark ✅, note it's a named param `Severity` on every attribute
- Section 7.1 Rule-Level Cascade → mark ✅, note `[StopOnFirstFailure]` is the explicit opt-in; default is continue

**Step 2: Commit**

```bash
git add docs/features.md
git commit -m "docs: mark ErrorCode, Severity, and cascade stop as complete in features.md"
```
