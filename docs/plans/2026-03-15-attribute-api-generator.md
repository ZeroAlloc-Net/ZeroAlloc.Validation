# Attribute API & Source Generator Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement attribute-based validation rules and a Roslyn source generator that emits zero-allocation `Validate()` implementations.

**Architecture:** Models opt in with `[Validate]`; properties declare rules with attributes (`[NotEmpty]`, `[MaxLength(100)]`, etc.). The incremental source generator reads these attributes at compile time and emits `sealed partial class {Name}Validator : ValidatorFor<T>` with a `stackalloc`-backed `Validate()` body — no reflection, no heap allocation on the happy path.

**Tech Stack:** C# 13, .NET 8/9/10, Roslyn incremental source generators (`IIncrementalGenerator`), `Microsoft.CodeAnalysis.CSharp` 4.11.0, xUnit 2.9.3.

---

## Reference

Design doc: `docs/plans/2026-03-15-attribute-api-generator-design.md`

Existing files to know:
- `src/ZValidation/Core/ValidationFailure.cs` — `readonly struct ValidationFailure { PropertyName, ErrorMessage, ErrorCode?, Severity }`
- `src/ZValidation/Core/ValidationResult.cs` — `readonly struct ValidationResult(ValidationFailure[])` with `IsValid` and `ReadOnlySpan<ValidationFailure> Failures`
- `src/ZValidation/Core/ValidatorFor.cs` — `abstract partial class ValidatorFor<T> { abstract ValidationResult Validate(T); }`
- `src/ZValidation.Generator/ValidatorGenerator.cs` — stub `IIncrementalGenerator` with empty `Initialize`
- `tests/ZValidation.Tests/` — xUnit project referencing `ZValidation` and `ZValidation.Testing`

---

### Task 1: `ValidationAttribute` base class and `[Validate]` marker

**Files:**
- Create: `src/ZValidation/Attributes/ValidationAttribute.cs`
- Create: `src/ZValidation/Attributes/ValidateAttribute.cs`
- Test: `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`

**Step 1: Write the failing test**

Create `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`:

```csharp
using ZValidation;

namespace ZValidation.Tests.Attributes;

public class AttributeDeclarationTests
{
    [Fact]
    public void ValidateAttribute_CanBeAppliedToClass()
    {
        var attrs = typeof(SampleModel).GetCustomAttributes(typeof(ValidateAttribute), false);
        Assert.Single(attrs);
    }

    [Fact]
    public void ValidationAttribute_ExposesMessageProperty()
    {
        var attr = new NotEmptyAttribute { Message = "custom" };
        Assert.Equal("custom", attr.Message);
    }

    [Validate]
    private class SampleModel { }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: FAIL — `ValidateAttribute` and `NotEmptyAttribute` not defined.

**Step 3: Create `ValidationAttribute` base**

Create `src/ZValidation/Attributes/ValidationAttribute.cs`:

```csharp
namespace ZValidation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class ValidationAttribute : Attribute
{
    public string? Message { get; set; }
}
```

**Step 4: Create `[Validate]` marker**

Create `src/ZValidation/Attributes/ValidateAttribute.cs`:

```csharp
namespace ZValidation;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ValidateAttribute : Attribute { }
```

**Step 5: Run test to verify it passes**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: 2 of 2 pass (NotEmptyAttribute test will still fail — that's fine, it's tested in Task 2).

Actually: remove the `NotEmptyAttribute` test for now — add it in Task 2. Keep only `ValidateAttribute_CanBeAppliedToClass`.

**Step 6: Commit**

```bash
git add src/ZValidation/Attributes/ tests/ZValidation.Tests/Attributes/
git commit -m "feat: add ValidationAttribute base and [Validate] marker"
```

---

### Task 2: Null / Empty attributes — `[NotNull]` and `[NotEmpty]`

**Files:**
- Create: `src/ZValidation/Attributes/NotNullAttribute.cs`
- Create: `src/ZValidation/Attributes/NotEmptyAttribute.cs`
- Modify: `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`

**Step 1: Write the failing test**

Add to `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`:

```csharp
[Fact]
public void NotNullAttribute_CanBeAppliedToProperty()
{
    var prop = typeof(NullModel).GetProperty(nameof(NullModel.Value))!;
    var attr = prop.GetCustomAttribute<NotNullAttribute>();
    Assert.NotNull(attr);
}

[Fact]
public void NotEmptyAttribute_MessageDefaultsToNull()
{
    var attr = new NotEmptyAttribute();
    Assert.Null(attr.Message);
}

[Fact]
public void NotEmptyAttribute_CanSetCustomMessage()
{
    var attr = new NotEmptyAttribute { Message = "Required" };
    Assert.Equal("Required", attr.Message);
}

private class NullModel
{
    [NotNull]
    public string? Value { get; set; }
}
```

**Step 2: Run to verify fails**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: FAIL — `NotNullAttribute`, `NotEmptyAttribute` not defined.

**Step 3: Create `NotNullAttribute`**

Create `src/ZValidation/Attributes/NotNullAttribute.cs`:

```csharp
namespace ZValidation;

public sealed class NotNullAttribute : ValidationAttribute { }
```

**Step 4: Create `NotEmptyAttribute`**

Create `src/ZValidation/Attributes/NotEmptyAttribute.cs`:

```csharp
namespace ZValidation;

public sealed class NotEmptyAttribute : ValidationAttribute { }
```

**Step 5: Run to verify passes**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: All pass.

**Step 6: Commit**

```bash
git add src/ZValidation/Attributes/ tests/ZValidation.Tests/Attributes/
git commit -m "feat: add [NotNull] and [NotEmpty] validation attributes"
```

---

### Task 3: String length attributes — `[MinLength]` and `[MaxLength]`

**Files:**
- Create: `src/ZValidation/Attributes/MinLengthAttribute.cs`
- Create: `src/ZValidation/Attributes/MaxLengthAttribute.cs`
- Modify: `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`

> **Note:** `MinLengthAttribute` and `MaxLengthAttribute` also exist in `System.ComponentModel.DataAnnotations`. These live in the `ZValidation` namespace. If a model file uses both, it must qualify with `ZValidation.MaxLengthAttribute` explicitly. This is acceptable — users of this library should not mix DataAnnotations and ZValidation.

**Step 1: Write the failing tests**

Add to `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`:

```csharp
[Fact]
public void MinLengthAttribute_StoresMinValue()
{
    var attr = new ZValidation.MinLengthAttribute(3);
    Assert.Equal(3, attr.Min);
}

[Fact]
public void MaxLengthAttribute_StoresMaxValue()
{
    var attr = new ZValidation.MaxLengthAttribute(100);
    Assert.Equal(100, attr.Max);
}
```

**Step 2: Run to verify fails**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: FAIL.

**Step 3: Create `MinLengthAttribute`**

Create `src/ZValidation/Attributes/MinLengthAttribute.cs`:

```csharp
namespace ZValidation;

public sealed class MinLengthAttribute(int min) : ValidationAttribute
{
    public int Min { get; } = min;
}
```

**Step 4: Create `MaxLengthAttribute`**

Create `src/ZValidation/Attributes/MaxLengthAttribute.cs`:

```csharp
namespace ZValidation;

public sealed class MaxLengthAttribute(int max) : ValidationAttribute
{
    public int Max { get; } = max;
}
```

**Step 5: Run to verify passes**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: All pass.

**Step 6: Commit**

```bash
git add src/ZValidation/Attributes/ tests/ZValidation.Tests/Attributes/
git commit -m "feat: add [MinLength] and [MaxLength] validation attributes"
```

---

### Task 4: Comparison attributes — `[GreaterThan]`, `[LessThan]`, `[InclusiveBetween]`

**Files:**
- Create: `src/ZValidation/Attributes/GreaterThanAttribute.cs`
- Create: `src/ZValidation/Attributes/LessThanAttribute.cs`
- Create: `src/ZValidation/Attributes/InclusiveBetweenAttribute.cs`
- Modify: `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`

**Step 1: Write the failing tests**

Add to `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`:

```csharp
[Fact]
public void GreaterThanAttribute_StoresValue()
{
    var attr = new GreaterThanAttribute(0);
    Assert.Equal(0.0, attr.Value);
}

[Fact]
public void LessThanAttribute_StoresValue()
{
    var attr = new LessThanAttribute(120);
    Assert.Equal(120.0, attr.Value);
}

[Fact]
public void InclusiveBetweenAttribute_StoresMinAndMax()
{
    var attr = new InclusiveBetweenAttribute(1, 100);
    Assert.Equal(1.0, attr.Min);
    Assert.Equal(100.0, attr.Max);
}
```

**Step 2: Run to verify fails**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: FAIL.

**Step 3: Create the three comparison attributes**

Create `src/ZValidation/Attributes/GreaterThanAttribute.cs`:

```csharp
namespace ZValidation;

public sealed class GreaterThanAttribute(double value) : ValidationAttribute
{
    public double Value { get; } = value;
}
```

Create `src/ZValidation/Attributes/LessThanAttribute.cs`:

```csharp
namespace ZValidation;

public sealed class LessThanAttribute(double value) : ValidationAttribute
{
    public double Value { get; } = value;
}
```

Create `src/ZValidation/Attributes/InclusiveBetweenAttribute.cs`:

```csharp
namespace ZValidation;

public sealed class InclusiveBetweenAttribute(double min, double max) : ValidationAttribute
{
    public double Min { get; } = min;
    public double Max { get; } = max;
}
```

**Step 4: Run to verify passes**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: All pass.

**Step 5: Commit**

```bash
git add src/ZValidation/Attributes/ tests/ZValidation.Tests/Attributes/
git commit -m "feat: add [GreaterThan], [LessThan], [InclusiveBetween] validation attributes"
```

---

### Task 5: Format attributes — `[EmailAddress]` and `[Matches]`

**Files:**
- Create: `src/ZValidation/Attributes/EmailAddressAttribute.cs`
- Create: `src/ZValidation/Attributes/MatchesAttribute.cs`
- Modify: `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`

**Step 1: Write the failing tests**

Add to `tests/ZValidation.Tests/Attributes/AttributeDeclarationTests.cs`:

```csharp
[Fact]
public void EmailAddressAttribute_CanBeCreated()
{
    var attr = new ZValidation.EmailAddressAttribute();
    Assert.Null(attr.Message);
}

[Fact]
public void MatchesAttribute_StoresPattern()
{
    var attr = new MatchesAttribute(@"^\d{4}$");
    Assert.Equal(@"^\d{4}$", attr.Pattern);
}
```

> **Note:** `EmailAddressAttribute` also exists in `System.ComponentModel.DataAnnotations`. Use `ZValidation.EmailAddressAttribute` explicitly in tests to avoid ambiguity.

**Step 2: Run to verify fails**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: FAIL.

**Step 3: Create `EmailAddressAttribute`**

Create `src/ZValidation/Attributes/EmailAddressAttribute.cs`:

```csharp
namespace ZValidation;

public sealed class EmailAddressAttribute : ValidationAttribute { }
```

**Step 4: Create `MatchesAttribute`**

Create `src/ZValidation/Attributes/MatchesAttribute.cs`:

```csharp
namespace ZValidation;

public sealed class MatchesAttribute(string pattern) : ValidationAttribute
{
    public string Pattern { get; } = pattern;
}
```

**Step 5: Run to verify passes**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AttributeDeclarationTests"
```

Expected: All pass.

**Step 6: Build entire solution to confirm no regressions**

```bash
dotnet build ZValidation.slnx
```

Expected: 0 errors, 0 warnings.

**Step 7: Commit**

```bash
git add src/ZValidation/Attributes/ tests/ZValidation.Tests/Attributes/
git commit -m "feat: add [EmailAddress] and [Matches] validation attributes"
```

---

### Task 6: Generator — discover `[Validate]` classes

**Files:**
- Modify: `src/ZValidation.Generator/ValidatorGenerator.cs`
- Create: `src/ZValidation.Generator/ValidatorGeneratorTests.cs` (generator unit test helper — see below)
- Test: `tests/ZValidation.Tests/Generator/GeneratorDiscoveryTests.cs`

The generator uses Roslyn's incremental pipeline. This task wires up the discovery step only — no code emission yet.

**Step 1: Write the failing test**

Create `tests/ZValidation.Tests/Generator/GeneratorDiscoveryTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ZValidation.Generator;

namespace ZValidation.Tests.Generator;

public class GeneratorDiscoveryTests
{
    [Fact]
    public void Generator_ProducesOutput_ForValidateClass()
    {
        var source = """
            using ZValidation;

            namespace TestModels;

            [Validate]
            public class Person
            {
                [NotEmpty]
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_ProducesNoOutput_ForClassWithoutValidateAttribute()
    {
        var source = """
            namespace TestModels;
            public class Person { public string Name { get; set; } = ""; }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(compilation);

        return driver.GetRunResult();
    }
}
```

Add `Microsoft.CodeAnalysis.CSharp` reference to `tests/ZValidation.Tests/ZValidation.Tests.csproj`:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" />
```

**Step 2: Run to verify fails**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "GeneratorDiscoveryTests"
```

Expected: FAIL — generator produces no output.

**Step 3: Implement discovery in the generator**

Replace the body of `src/ZValidation.Generator/ValidatorGenerator.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZValidation.Generator;

[Generator]
public sealed class ValidatorGenerator : IIncrementalGenerator
{
    private const string ValidateAttributeFqn = "ZValidation.ValidateAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validateClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Where(static s => s is not null);

        context.RegisterSourceOutput(validateClasses, Emit);
    }

    private static void Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol)
    {
        // Emission implemented in Task 7.
        ctx.AddSource($"{classSymbol.Name}Validator.g.cs", "// placeholder");
    }
}
```

**Step 4: Run to verify passes**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "GeneratorDiscoveryTests"
```

Expected: Both tests pass.

**Step 5: Commit**

```bash
git add src/ZValidation.Generator/ tests/ZValidation.Tests/Generator/
git commit -m "feat: generator discovers [Validate] classes via incremental pipeline"
```

---

### Task 7: Generator — emit validator class shell

**Files:**
- Modify: `src/ZValidation.Generator/ValidatorGenerator.cs`
- Modify: `tests/ZValidation.Tests/Generator/GeneratorDiscoveryTests.cs`

**Step 1: Write the failing test**

Add to `tests/ZValidation.Tests/Generator/GeneratorDiscoveryTests.cs`:

```csharp
[Fact]
public void Generator_EmitsValidatorClass_InSameNamespace()
{
    var source = """
        using ZValidation;
        namespace TestModels;

        [Validate]
        public class Person
        {
            [NotEmpty]
            public string Name { get; set; } = "";
        }
        """;

    var result = RunGenerator(source);
    var generated = result.GeneratedTrees[0].ToString();

    Assert.Contains("namespace TestModels", generated);
    Assert.Contains("class PersonValidator", generated);
    Assert.Contains("ValidatorFor<Person>", generated);
}
```

**Step 2: Run to verify fails**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "Generator_EmitsValidatorClass"
```

Expected: FAIL — generated output is just `// placeholder`.

**Step 3: Implement class shell emission**

Replace the `Emit` method in `ValidatorGenerator.cs`:

```csharp
private static void Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol)
{
    var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
        ? null
        : classSymbol.ContainingNamespace.ToDisplayString();

    var validatorName = $"{classSymbol.Name}Validator";
    var modelName = classSymbol.Name;

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("using ZValidation;");
    sb.AppendLine();

    if (namespaceName is not null)
    {
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
    }

    sb.AppendLine($"public sealed partial class {validatorName} : ValidatorFor<{modelName}>");
    sb.AppendLine("{");
    sb.AppendLine($"    public override global::ZValidation.ValidationResult Validate({modelName} instance)");
    sb.AppendLine("    {");
    sb.AppendLine("        // TODO: rule emission (Task 8)");
    sb.AppendLine("        return new global::ZValidation.ValidationResult([]);");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    ctx.AddSource($"{validatorName}.g.cs", sb.ToString());
}
```

**Step 4: Run to verify passes**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "GeneratorDiscoveryTests"
```

Expected: All pass.

**Step 5: Commit**

```bash
git add src/ZValidation.Generator/
git commit -m "feat: generator emits validator class shell with correct namespace and base type"
```

---

### Task 8: Generator — emit `Validate()` rule body

**Files:**
- Modify: `src/ZValidation.Generator/ValidatorGenerator.cs`
- Create: `src/ZValidation.Generator/RuleEmitter.cs`
- Create: `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write the failing integration tests**

Create `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ZValidation.Generator;

namespace ZValidation.Tests.Generator;

public class GeneratorRuleEmissionTests
{
    [Fact]
    public void Generator_EmitsNotEmpty_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("IsNullOrEmpty", generated);
        Assert.Contains("\"Name\"", generated);
    }

    [Fact]
    public void Generator_EmitsMaxLength_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person { [MaxLength(50)] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains(".Length > 50", generated);
    }

    [Fact]
    public void Generator_EmitsGreaterThan_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person { [GreaterThan(0)] public int Age { get; set; } }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("<= 0", generated);
    }

    [Fact]
    public void Generator_EmitsStopAtFirstFailure_AsElseIf()
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
        Assert.Contains("else if", generated);
    }

    [Fact]
    public void Generator_EmitsStackalloc_SizedToRuleCount()
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
                [GreaterThan(0)]
                public int Age { get; set; }
            }
            """;

        // 3 rules total
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("stackalloc ValidationFailure[3]", generated);
    }

    private static string RunGeneratorGetSource(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var result = driver.GetRunResult();
        return result.GeneratedTrees[0].ToString();
    }
}
```

**Step 2: Run to verify fails**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "GeneratorRuleEmissionTests"
```

Expected: FAIL — generated body is still the TODO stub.

**Step 3: Create `RuleEmitter` helper**

Create `src/ZValidation.Generator/RuleEmitter.cs`:

```csharp
using Microsoft.CodeAnalysis;
using System.Text;

namespace ZValidation.Generator;

internal static class RuleEmitter
{
    private const string NotNullFqn          = "ZValidation.NotNullAttribute";
    private const string NotEmptyFqn         = "ZValidation.NotEmptyAttribute";
    private const string MinLengthFqn        = "ZValidation.MinLengthAttribute";
    private const string MaxLengthFqn        = "ZValidation.MaxLengthAttribute";
    private const string GreaterThanFqn      = "ZValidation.GreaterThanAttribute";
    private const string LessThanFqn         = "ZValidation.LessThanAttribute";
    private const string InclusiveBetweenFqn = "ZValidation.InclusiveBetweenAttribute";
    private const string EmailAddressFqn     = "ZValidation.EmailAddressAttribute";
    private const string MatchesFqn          = "ZValidation.MatchesAttribute";

    /// <summary>
    /// Returns all rule attributes on a property in declaration order,
    /// paired with the property symbol.
    /// </summary>
    public static IReadOnlyList<(IPropertySymbol Property, AttributeData Rule)> GetRules(INamedTypeSymbol classSymbol)
    {
        var rules = new List<(IPropertySymbol, AttributeData)>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            foreach (var attr in prop.GetAttributes())
            {
                if (IsRuleAttribute(attr))
                    rules.Add((prop, attr));
            }
        }
        return rules;
    }

    private static bool IsRuleAttribute(AttributeData attr)
    {
        var fqn = attr.AttributeClass?.ToDisplayString();
        return fqn is NotNullFqn or NotEmptyFqn or MinLengthFqn or MaxLengthFqn
            or GreaterThanFqn or LessThanFqn or InclusiveBetweenFqn
            or EmailAddressFqn or MatchesFqn;
    }

    /// <summary>
    /// Emits the body of the Validate() method into <paramref name="sb"/>.
    /// </summary>
    public static void EmitValidateBody(
        StringBuilder sb,
        INamedTypeSymbol classSymbol,
        string modelParamName = "instance")
    {
        // Group rules by property, preserving order
        var byProperty = new List<(IPropertySymbol Property, List<AttributeData> Rules)>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            var propRules = prop.GetAttributes()
                .Where(IsRuleAttribute)
                .ToList();
            if (propRules.Count > 0)
                byProperty.Add((prop, propRules));
        }

        int totalRules = byProperty.Sum(x => x.Rules.Count);

        sb.AppendLine($"        Span<global::ZValidation.ValidationFailure> buffer = stackalloc global::ZValidation.ValidationFailure[{totalRules}];");
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

                var condition = BuildCondition(fqn, attr, propAccess, prop.Type);
                sb.AppendLine($"{prefix} ({condition})");
                sb.AppendLine($"            buffer[count++] = new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = \"{EscapeString(message)}\" }};");
            }
            sb.AppendLine();
        }

        sb.AppendLine("        return new global::ZValidation.ValidationResult(buffer[..count].ToArray());");
    }

    private static string? GetMessage(AttributeData attr)
    {
        foreach (var named in attr.NamedArguments)
            if (named.Key == "Message" && named.Value.Value is string s)
                return s;
        return null;
    }

    private static string BuildCondition(string fqn, AttributeData attr, string access, ITypeSymbol type)
    {
        return fqn switch
        {
            NotNullFqn          => $"{access} is null",
            NotEmptyFqn         => $"string.IsNullOrEmpty({access})",
            MinLengthFqn        => $"{access}.Length < {(int)attr.ConstructorArguments[0].Value!}",
            MaxLengthFqn        => $"{access}.Length > {(int)attr.ConstructorArguments[0].Value!}",
            GreaterThanFqn      => $"System.Convert.ToDouble({access}) <= {(double)attr.ConstructorArguments[0].Value!}",
            LessThanFqn         => $"System.Convert.ToDouble({access}) >= {(double)attr.ConstructorArguments[0].Value!}",
            InclusiveBetweenFqn =>
                $"System.Convert.ToDouble({access}) < {(double)attr.ConstructorArguments[0].Value!} || System.Convert.ToDouble({access}) > {(double)attr.ConstructorArguments[1].Value!}",
            EmailAddressFqn     => $"!ZValidationInternal.EmailValidator.IsValid({access})",
            MatchesFqn          => $"!global::System.Text.RegularExpressions.Regex.IsMatch({access} ?? \"\", \"{EscapeString((string)attr.ConstructorArguments[0].Value!)}\") ",
            _                   => "false"
        };
    }

    private static string GetDefaultMessage(string fqn, AttributeData attr, string propName) =>
        fqn switch
        {
            NotNullFqn          => $"{propName} must not be null.",
            NotEmptyFqn         => $"{propName} must not be empty.",
            MinLengthFqn        => $"{propName} must be at least {attr.ConstructorArguments[0].Value} characters.",
            MaxLengthFqn        => $"{propName} must not exceed {attr.ConstructorArguments[0].Value} characters.",
            GreaterThanFqn      => $"{propName} must be greater than {attr.ConstructorArguments[0].Value}.",
            LessThanFqn         => $"{propName} must be less than {attr.ConstructorArguments[0].Value}.",
            InclusiveBetweenFqn => $"{propName} must be between {attr.ConstructorArguments[0].Value} and {attr.ConstructorArguments[1].Value}.",
            EmailAddressFqn     => $"{propName} must be a valid email address.",
            MatchesFqn          => $"{propName} does not match the required pattern.",
            _                   => $"{propName} is invalid."
        };

    private static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
```

**Step 4: Create `EmailValidator` internal helper in `ZValidation`**

The generator emits calls to `ZValidationInternal.EmailValidator.IsValid()` — a zero-alloc email check (no regex). Create it in the core library:

Create `src/ZValidation/Internal/EmailValidator.cs`:

```csharp
namespace ZValidationInternal;

internal static class EmailValidator
{
    // Simple zero-alloc check: must contain exactly one '@' with at least one char before and one '.' after.
    internal static bool IsValid(string? email)
    {
        if (string.IsNullOrEmpty(email)) return false;
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return false;
        var domainPart = email.AsSpan(atIndex + 1);
        var dotIndex = domainPart.LastIndexOf('.');
        return dotIndex > 0 && dotIndex < domainPart.Length - 1;
    }
}
```

**Step 5: Wire `RuleEmitter` into the generator's `Emit` method**

Replace the `Emit` method in `ValidatorGenerator.cs`:

```csharp
private static void Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol)
{
    var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
        ? null
        : classSymbol.ContainingNamespace.ToDisplayString();

    var validatorName = $"{classSymbol.Name}Validator";
    var modelName = classSymbol.Name;

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("using ZValidation;");
    sb.AppendLine();

    if (namespaceName is not null)
    {
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
    }

    sb.AppendLine($"public sealed partial class {validatorName} : ValidatorFor<{modelName}>");
    sb.AppendLine("{");
    sb.AppendLine($"    public override global::ZValidation.ValidationResult Validate({modelName} instance)");
    sb.AppendLine("    {");

    RuleEmitter.EmitValidateBody(sb, classSymbol);

    sb.AppendLine("    }");
    sb.AppendLine("}");

    ctx.AddSource($"{validatorName}.g.cs", sb.ToString());
}
```

**Step 6: Run to verify generator tests pass**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "GeneratorRuleEmissionTests"
```

Expected: All 5 pass.

**Step 7: Commit**

```bash
git add src/ZValidation.Generator/ src/ZValidation/Internal/
git commit -m "feat: generator emits zero-alloc Validate() body with stackalloc buffer"
```

---

### Task 9: End-to-end integration tests

Validate the full pipeline: model with attributes → generator runs at build time → validator works correctly.

**Files:**
- Create: `tests/ZValidation.Tests/Integration/EndToEndTests.cs`

These tests use a model defined in the test project itself. Because `ZValidation.Tests` references `ZValidation` (which bundles the generator), the generator runs during the test project's build and produces `PersonValidator` automatically.

**Step 1: Write the failing integration tests**

Create `tests/ZValidation.Tests/Integration/EndToEndTests.cs`:

```csharp
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

// The generator will emit PersonValidator for this model.
[Validate]
public class Person
{
    [NotEmpty(Message = "Name is required.")]
    [ZValidation.MaxLength(100)]
    public string Name { get; set; } = "";

    [ZValidation.EmailAddress]
    public string Email { get; set; } = "";

    [GreaterThan(0)]
    [LessThan(120)]
    public int Age { get; set; }
}

public class EndToEndTests
{
    private readonly PersonValidator _validator = new();

    [Fact]
    public void Valid_Person_PassesValidation()
    {
        var person = new Person { Name = "Alice", Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Empty_Name_FailsWithCustomMessage()
    {
        var person = new Person { Name = "", Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "Name");
        Assert.Equal("Name is required.", result.Failures[0].ErrorMessage);
    }

    [Fact]
    public void Name_TooLong_FailsValidation()
    {
        var person = new Person { Name = new string('x', 101), Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        ValidationAssert.HasError(result, "Name");
    }

    [Fact]
    public void Name_BothEmptyAndTooLong_OnlyReportsFirstFailure()
    {
        // Empty string can't be too long — test that only one failure is reported per property
        var person = new Person { Name = "", Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        Assert.Equal(1, result.Failures.ToArray().Count(f => f.PropertyName == "Name"));
    }

    [Fact]
    public void Invalid_Email_FailsValidation()
    {
        var person = new Person { Name = "Alice", Email = "not-an-email", Age = 30 };
        var result = _validator.Validate(person);
        ValidationAssert.HasError(result, "Email");
    }

    [Fact]
    public void Age_Zero_FailsGreaterThan()
    {
        var person = new Person { Name = "Alice", Email = "alice@example.com", Age = 0 };
        var result = _validator.Validate(person);
        ValidationAssert.HasError(result, "Age");
    }

    [Fact]
    public void Multiple_Properties_Invalid_ReportsAllFailures()
    {
        var person = new Person { Name = "", Email = "bad", Age = -1 };
        var result = _validator.Validate(person);
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "Name");
        ValidationAssert.HasError(result, "Email");
        ValidationAssert.HasError(result, "Age");
    }
}
```

**Step 2: Run to verify fails**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "EndToEndTests"
```

Expected: FAIL (compilation error — `PersonValidator` not generated yet, or generator not fully wired).

**Step 3: Fix any issues surfaced by the integration test**

Run the build first to see generator output:

```bash
dotnet build tests/ZValidation.Tests/ZValidation.Tests.csproj
```

Inspect generated files in `tests/ZValidation.Tests/obj/Debug/netX.X/generated/`. Fix any issues in `ValidatorGenerator.cs` or `RuleEmitter.cs`.

**Step 4: Run all tests**

```bash
dotnet test ZValidation.slnx
```

Expected: All tests pass across all TFMs.

**Step 5: Commit**

```bash
git add tests/ZValidation.Tests/Integration/
git commit -m "test: add end-to-end integration tests for attribute-based validation"
```

---

### Task 10: Final verification

**Step 1: Full solution build**

```bash
dotnet build ZValidation.slnx
```

Expected: 0 errors, 0 warnings.

**Step 2: Full test run**

```bash
dotnet test ZValidation.slnx
```

Expected: All tests pass across net8.0, net9.0, net10.0.

**Step 3: Final commit**

```bash
git commit --allow-empty -m "chore: attribute API and source generator complete"
```
