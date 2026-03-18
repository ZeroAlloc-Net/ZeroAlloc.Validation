# Inject & Options Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add zero-friction DI registration (`ZeroAlloc.Validation.Inject`) and source-generated
options validation (`ZeroAlloc.Validation.Options`) with no manual registrations required.

**Architecture:** A shared `ValidatorRegistrationEmitter` helper in the Inject generator project
emits `TryAddSingleton<ValidatorFor<T>, TValidator>()` lines. The AspNetCore and Options generators
reference this helper project directly so the emit logic is never duplicated. All registration
methods use `TryAdd` throughout and are therefore idempotent and order-independent.

**Tech Stack:** C# 12, Roslyn incremental generators (`netstandard2.0`), xUnit,
`Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`

---

### Key files to read before starting

- `src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs` — generator being changed
- `tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs` — test pattern to follow
- `tests/ZeroAlloc.Validation.Tests.AspNetCore/TestApp.cs` — integration test app to update
- `ZeroAlloc.Validation.slnx` — solution file; new projects must be added here

---

### Task 1: Rename Z-prefixed names in AspNetCoreFilterEmitter

**Files:**
- Modify: `src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests.AspNetCore/TestApp.cs`

Three renames touch generated class/method names and their callers:

| Old | New |
|---|---|
| `ZValidationActionFilter` | `ZeroAllocValidationActionFilter` |
| `ZValidationServiceCollectionExtensions` | `ZeroAllocValidationServiceCollectionExtensions` |
| `AddZValidationAutoValidation` | `AddZeroAllocAspNetCoreValidation` |
| Source file hint `ZValidationActionFilter.g.cs` | `ZeroAllocValidationActionFilter.g.cs` |
| Source file hint `ZValidationServiceCollectionExtensions.g.cs` | `ZeroAllocValidationServiceCollectionExtensions.g.cs` |

**Step 1: Update failing tests first**

In `tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs`, update every
reference to the old names:

- Replace `"ZValidationActionFilter.g.cs"` with `"ZeroAllocValidationActionFilter.g.cs"`
- Replace `"ZValidationServiceCollectionExtensions.g.cs"` with `"ZeroAllocValidationServiceCollectionExtensions.g.cs"`
- Replace `"AddZValidationAutoValidation"` with `"AddZeroAllocAspNetCoreValidation"`

**Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "AspNetCoreGenerator" -v minimal
```

Expected: FAIL — old names not found in generated output.

**Step 3: Update AspNetCoreFilterEmitter**

In `src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs`:

Replace these four `AddSource` / `AppendLine` strings:

```csharp
// Line 31 — AddSource calls
ctx.AddSource("ZeroAlloc.Validation.ZeroAllocValidationActionFilter.g.cs",                EmitFilter(models));
ctx.AddSource("ZeroAlloc.Validation.ZeroAllocValidationServiceCollectionExtensions.g.cs", EmitExtensions(models));

// Line 52 — class declaration
sb.AppendLine("internal sealed class ZeroAllocValidationActionFilter : global::Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter");

// Line 56 — constructor
sb.AppendLine("    public ZeroAllocValidationActionFilter(global::System.IServiceProvider services) => _services = services;");

// Line 114 — extensions class
sb.AppendLine("public static class ZeroAllocValidationServiceCollectionExtensions");

// Line 116 — extension method
sb.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZeroAllocAspNetCoreValidation(");

// Line 128 — TryAddTransient for filter
sb.AppendLine("        services.TryAddTransient<ZeroAllocValidationActionFilter>();");

// Line 129 — MvcOptions filter registration
sb.AppendLine("        services.Configure<global::Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.Add<ZeroAllocValidationActionFilter>());");
```

**Step 4: Run tests to confirm they pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "AspNetCoreGenerator" -v minimal
```

Expected: all 5 tests PASS.

**Step 5: Update integration test app**

In `tests/ZeroAlloc.Validation.Tests.AspNetCore/TestApp.cs`, replace:

```csharp
builder.Services.AddZValidationAutoValidation();
```

With:

```csharp
builder.Services.AddZeroAllocAspNetCoreValidation();
```

**Step 6: Run full AspNetCore integration tests**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests.AspNetCore -v minimal
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all tests pass.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs \
        tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs \
        tests/ZeroAlloc.Validation.Tests.AspNetCore/TestApp.cs
git commit -m "feat!: rename Z-prefixed generated names to ZeroAlloc-prefixed"
```

---

### Task 2: Create ZeroAlloc.Validation.Inject generator project

**Files:**
- Create: `src/ZeroAlloc.Validation.Inject/ZeroAlloc.Validation.Inject.csproj`
- Create: `src/ZeroAlloc.Validation.Inject/ValidatorRegistrationEmitter.cs`
- Create: `src/ZeroAlloc.Validation.Inject/InjectGenerator.cs`
- Modify: `ZeroAlloc.Validation.slnx`
- Test: `tests/ZeroAlloc.Validation.Tests/Generator/InjectGeneratorTests.cs` (create)

**Step 1: Create the csproj**

Create `src/ZeroAlloc.Validation.Inject/ZeroAlloc.Validation.Inject.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
    <PackageId>ZeroAlloc.Validation.Inject</PackageId>
    <Description>Source generator that emits AddZeroAllocValidators() — zero-reflection DI registration for all ZeroAlloc.Validation validators.</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="5.3.0" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

**Step 2: Create ValidatorRegistrationEmitter (the shared helper)**

Create `src/ZeroAlloc.Validation.Inject/ValidatorRegistrationEmitter.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Inject;

/// <summary>
/// Shared helper used by InjectGenerator, AspNetCoreFilterEmitter, and OptionsValidationEmitter.
/// Emits one TryAddSingleton line per [Validate] class, registering the generated validator
/// as ValidatorFor&lt;T&gt; so any DI consumer can resolve it by the abstract base type.
/// </summary>
internal static class ValidatorRegistrationEmitter
{
    /// <summary>
    /// Appends one <c>services.TryAddSingleton&lt;ValidatorFor&lt;T&gt;, TValidator&gt;();</c>
    /// line per model into <paramref name="sb"/>.
    /// </summary>
    public static void EmitRegistrations(StringBuilder sb, IEnumerable<INamedTypeSymbol> models)
    {
        foreach (var model in models)
        {
            var modelFqn     = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var validatorFqn = model.ContainingNamespace.IsGlobalNamespace
                ? $"global::{model.Name}Validator"
                : $"global::{model.ContainingNamespace.ToDisplayString()}.{model.Name}Validator";

            sb.AppendLine(
                $"        services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<{modelFqn}>, {validatorFqn}>();");
        }
    }
}
```

**Step 3: Create InjectGenerator**

Create `src/ZeroAlloc.Validation.Inject/InjectGenerator.cs`:

```csharp
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Validation.Inject;

[Generator]
public sealed class InjectGenerator : IIncrementalGenerator
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validateClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

#pragma warning disable EPS06
        var collected = validateClasses.Collect();
#pragma warning restore EPS06
        context.RegisterSourceOutput(collected, Emit);
    }

    private static void Emit(SourceProductionContext ctx, ImmutableArray<INamedTypeSymbol> models)
    {
        if (models.IsDefaultOrEmpty) return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine();
        sb.AppendLine("public static class ZeroAllocValidatorRegistrationExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZeroAllocValidators(");
        sb.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("    {");
        ValidatorRegistrationEmitter.EmitRegistrations(sb, models);
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        ctx.AddSource("ZeroAlloc.Validation.ZeroAllocValidatorRegistrationExtensions.g.cs", sb.ToString());
    }
}
```

**Step 4: Add to solution**

In `ZeroAlloc.Validation.slnx`, add inside the `<Folder Name="/src/">` block:

```xml
<Project Path="src/ZeroAlloc.Validation.Inject/ZeroAlloc.Validation.Inject.csproj" />
```

**Step 5: Write failing generator tests**

Create `tests/ZeroAlloc.Validation.Tests/Generator/InjectGeneratorTests.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Generator;

public class InjectGeneratorTests
{
    [Fact]
    public void Generator_EmitsAddZeroAllocValidators_WithTwoModels()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            [Validate] public class Order    { [NotEmpty] public string Ref  { get; set; } = ""; }
            """;

        var generated = RunInjectGenerator(source);

        Assert.Contains("AddZeroAllocValidators", generated, System.StringComparison.Ordinal);
        Assert.Contains("TryAddSingleton", generated, System.StringComparison.Ordinal);
        Assert.Contains("ValidatorFor<global::MyApp.Customer>", generated, System.StringComparison.Ordinal);
        Assert.Contains("ValidatorFor<global::MyApp.Order>",    generated, System.StringComparison.Ordinal);
        Assert.Contains("global::MyApp.CustomerValidator",      generated, System.StringComparison.Ordinal);
        Assert.Contains("global::MyApp.OrderValidator",         generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NonValidateType_NotRegistered()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            public class NotAModel { public string X { get; set; } = ""; }
            """;

        var generated = RunInjectGenerator(source);

        Assert.DoesNotContain("NotAModel", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NoValidateClasses_EmitsNothing()
    {
        var source = """
            namespace MyApp;
            public class Plain { public string X { get; set; } = ""; }
            """;

        var trees = RunInjectGeneratorAllTrees(source);

        Assert.Empty(trees);
    }

    private static string RunInjectGenerator(string source)
        => RunInjectGeneratorAllTrees(source).First();

    private static System.Collections.Generic.IReadOnlyList<string> RunInjectGeneratorAllTrees(string source)
    {
        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ZeroAlloc.Validation.Inject.InjectGenerator();
        var driver    = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
    }
}
```

**Step 6: Add Inject generator reference to the test project**

In `tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj`, add inside the existing `<ItemGroup>` with project references:

```xml
<ProjectReference Include="..\..\src\ZeroAlloc.Validation.Inject\ZeroAlloc.Validation.Inject.csproj"
                  ReferenceOutputAssembly="true" />
```

**Step 7: Run tests to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "InjectGeneratorTests" -v minimal
```

Expected: FAIL — `InjectGenerator` does not exist yet (test project references the not-yet-compiled project).

**Step 8: Build the Inject project to confirm it compiles**

```bash
dotnet build src/ZeroAlloc.Validation.Inject/ZeroAlloc.Validation.Inject.csproj
```

Expected: succeeds with no errors.

**Step 9: Run tests to verify they pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "InjectGeneratorTests" -v minimal
```

Expected: all 3 tests PASS.

**Step 10: Run full suite for regressions**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all tests pass.

**Step 11: Commit**

```bash
git add src/ZeroAlloc.Validation.Inject/ \
        tests/ZeroAlloc.Validation.Tests/Generator/InjectGeneratorTests.cs \
        tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj \
        ZeroAlloc.Validation.slnx
git commit -m "feat: add ZeroAlloc.Validation.Inject source generator with AddZeroAllocValidators()"
```

---

### Task 3: Wire shared helper into AspNetCoreFilterEmitter + switch to ValidatorFor<T> registration

**Files:**
- Modify: `src/ZeroAlloc.Validation.AspNetCore.Generator/ZeroAlloc.Validation.AspNetCore.Generator.csproj`
- Modify: `src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs`

**Background:** The current filter resolves validators by concrete type (`GetRequiredService<CustomerValidator>()`).
We switch to resolving by `ValidatorFor<T>` so that registrations from all three packages are compatible.
The `EmitExtensions` method stops emitting its own `TryAddTransient` lines and uses `ValidatorRegistrationEmitter`
instead, which emits `TryAddSingleton<ValidatorFor<T>, TValidator>`.

**Step 1: Add Inject project reference to AspNetCore.Generator**

In `src/ZeroAlloc.Validation.AspNetCore.Generator/ZeroAlloc.Validation.AspNetCore.Generator.csproj`,
add a new `<ItemGroup>`:

```xml
<ItemGroup>
  <!-- Share the registration emit helper — not a runtime reference -->
  <ProjectReference Include="..\ZeroAlloc.Validation.Inject\ZeroAlloc.Validation.Inject.csproj"
                    ReferenceOutputAssembly="true" />
</ItemGroup>
```

**Step 2: Write failing tests**

In `tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs`, add two new tests:

```csharp
[Fact]
public void Generator_EmitsValidatorFor_InExtensionMethod()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace MyApp;
        [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
        """;

    var ext = RunAspNetGeneratorGetSource(source, "ZeroAllocValidationServiceCollectionExtensions.g.cs");
    Assert.Contains("ValidatorFor<global::MyApp.Customer>", ext, StringComparison.Ordinal);
    Assert.Contains("TryAddSingleton",                      ext, StringComparison.Ordinal);
}

[Fact]
public void Generator_DispatchUsesValidatorFor_NotConcreteType()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace MyApp;
        [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
        """;

    var filter = RunAspNetGeneratorGetSource(source, "ZeroAllocValidationActionFilter.g.cs");
    Assert.Contains("ValidatorFor<global::MyApp.Customer>", filter, StringComparison.Ordinal);
}
```

**Step 3: Run tests to confirm they fail**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "Generator_EmitsValidatorFor|Generator_DispatchUses" -v minimal
```

Expected: FAIL.

**Step 4: Update AspNetCoreFilterEmitter**

In `src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs`:

Add `using ZeroAlloc.Validation.Inject;` at the top.

In `AppendDispatchSwitch`, change the per-model line from:

```csharp
sb.AppendLine($"                return await _services.GetRequiredService<{validatorName}>().ValidateAsync({varName});");
```

To:

```csharp
sb.AppendLine($"                return await _services.GetRequiredService<global::ZeroAlloc.Validation.ValidatorFor<{fullName}>>().ValidateAsync({varName});");
```

In `EmitExtensions`, replace the per-model `foreach` loop:

```csharp
// Remove this block:
foreach (var model in models)
{
    var validatorName = ...
    sb.AppendLine($"        services.TryAddTransient<{validatorName}>();");
}

// Replace with:
ValidatorRegistrationEmitter.EmitRegistrations(sb, models);
```

**Step 5: Run new tests to confirm they pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "AspNetCoreGenerator" -v minimal
```

Expected: all 7 tests PASS.

**Step 6: Run full test suite + AspNetCore integration tests**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
dotnet test tests/ZeroAlloc.Validation.Tests.AspNetCore -v minimal
```

Expected: all pass.

**Step 7: Commit**

```bash
git add src/ZeroAlloc.Validation.AspNetCore.Generator/ \
        tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs
git commit -m "feat: wire ValidatorRegistrationEmitter into AspNetCore generator, resolve via ValidatorFor<T>"
```

---

### Task 4: Create ZeroAlloc.Validation.Options runtime project

**Files:**
- Create: `src/ZeroAlloc.Validation.Options/ZeroAlloc.Validation.Options.csproj`
- Create: `src/ZeroAlloc.Validation.Options/ZeroAllocOptionsValidator.cs`
- Modify: `ZeroAlloc.Validation.slnx`

**Step 1: Create the csproj**

Create `src/ZeroAlloc.Validation.Options/ZeroAlloc.Validation.Options.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageId>ZeroAlloc.Validation.Options</PackageId>
    <Description>Source-generated options validation for ZeroAlloc.Validation. Plugs compile-time validators into Microsoft.Extensions.Options with zero reflection.</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ZeroAlloc.Validation\ZeroAlloc.Validation.csproj" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ZeroAlloc.Validation.Options.Generator\ZeroAlloc.Validation.Options.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

**Step 2: Create ZeroAllocOptionsValidator**

Create `src/ZeroAlloc.Validation.Options/ZeroAllocOptionsValidator.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace ZeroAlloc.Validation.Options;

/// <summary>
/// Bridges a <see cref="ValidatorFor{T}"/> into the <see cref="IValidateOptions{T}"/> pipeline.
/// Resolved from DI by the generated <c>ValidateWithZeroAlloc()</c> extension methods.
/// </summary>
public sealed class ZeroAllocOptionsValidator<T> : IValidateOptions<T> where T : class
{
    private readonly ValidatorFor<T> _validator;

    public ZeroAllocOptionsValidator(ValidatorFor<T> validator) => _validator = validator;

    public ValidateOptionsResult Validate(string? name, T options)
    {
        var result = _validator.Validate(options);
        if (result.IsValid)
            return ValidateOptionsResult.Success;

        var failures = result.Failures;
        var errors   = new string[failures.Length];
        for (int i = 0; i < failures.Length; i++)
            errors[i] = $"{failures[i].PropertyName}: {failures[i].ErrorMessage}";

        return ValidateOptionsResult.Fail(errors);
    }
}
```

**Step 3: Add to solution**

In `ZeroAlloc.Validation.slnx`, add inside the `<Folder Name="/src/">` block:

```xml
<Project Path="src/ZeroAlloc.Validation.Options/ZeroAlloc.Validation.Options.csproj" />
```

**Step 4: Build to verify it compiles (generator project doesn't exist yet — add a stub)**

The `Options.csproj` references `Options.Generator` which doesn't exist yet. Create a minimal stub
csproj at `src/ZeroAlloc.Validation.Options.Generator/ZeroAlloc.Validation.Options.Generator.csproj`
(full implementation in Task 5):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="5.3.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

```bash
dotnet build src/ZeroAlloc.Validation.Options/ZeroAlloc.Validation.Options.csproj
```

Expected: succeeds.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Validation.Options/ \
        src/ZeroAlloc.Validation.Options.Generator/ \
        ZeroAlloc.Validation.slnx
git commit -m "feat: add ZeroAlloc.Validation.Options runtime project with ZeroAllocOptionsValidator<T>"
```

---

### Task 5: Create ZeroAlloc.Validation.Options.Generator

**Files:**
- Modify: `src/ZeroAlloc.Validation.Options.Generator/ZeroAlloc.Validation.Options.Generator.csproj`
- Create: `src/ZeroAlloc.Validation.Options.Generator/OptionsValidationEmitter.cs`
- Modify: `ZeroAlloc.Validation.slnx`
- Test: `tests/ZeroAlloc.Validation.Tests/Generator/OptionsGeneratorTests.cs` (create)

**Background:** For each `[Validate]` class the generator emits a strongly-typed
`ValidateWithZeroAlloc()` overload on `OptionsBuilder<T>`. The overload registers both
`ValidatorFor<T>` (via `ValidatorRegistrationEmitter`) and `IValidateOptions<T>` using `TryAdd`.
No generic fallback is emitted — if a type has no `[Validate]`, no overload exists and the
compiler reports a missing-overload error at the call site.

**Step 1: Replace stub csproj with full csproj**

Replace `src/ZeroAlloc.Validation.Options.Generator/ZeroAlloc.Validation.Options.Generator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="5.3.0" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- Access ValidatorRegistrationEmitter — compile-time reference only -->
    <ProjectReference Include="..\ZeroAlloc.Validation.Inject\ZeroAlloc.Validation.Inject.csproj"
                      ReferenceOutputAssembly="true" />
  </ItemGroup>

</Project>
```

**Step 2: Add to solution**

In `ZeroAlloc.Validation.slnx`, add inside the `<Folder Name="/src/">` block:

```xml
<Project Path="src/ZeroAlloc.Validation.Options.Generator/ZeroAlloc.Validation.Options.Generator.csproj" />
```

**Step 3: Write failing tests**

Create `tests/ZeroAlloc.Validation.Tests/Generator/OptionsGeneratorTests.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Generator;

public class OptionsGeneratorTests
{
    [Fact]
    public void Generator_EmitsValidateWithZeroAlloc_ForValidateClass()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("ValidateWithZeroAlloc",                             generated, System.StringComparison.Ordinal);
        Assert.Contains("OptionsBuilder<global::MyApp.DatabaseOptions>",     generated, System.StringComparison.Ordinal);
        Assert.Contains("IValidateOptions<global::MyApp.DatabaseOptions>",   generated, System.StringComparison.Ordinal);
        Assert.Contains("ZeroAllocOptionsValidator<global::MyApp.DatabaseOptions>", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsValidatorFor_TryAddSingleton()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class SmtpOptions { [NotEmpty] public string Host { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::MyApp.SmtpOptions>", generated, System.StringComparison.Ordinal);
        Assert.Contains("global::MyApp.SmtpOptionsValidator",                                                   generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsTwoOverloads_ForTwoValidateClasses()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            [Validate] public class SmtpOptions     { [NotEmpty] public string Host             { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("OptionsBuilder<global::MyApp.DatabaseOptions>", generated, System.StringComparison.Ordinal);
        Assert.Contains("OptionsBuilder<global::MyApp.SmtpOptions>",     generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NonValidateType_NoOverloadEmitted()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            public class NotOptions { public string X { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.DoesNotContain("NotOptions", generated, System.StringComparison.Ordinal);
    }

    private static string RunOptionsGenerator(string source)
    {
        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ZeroAlloc.Validation.Options.Generator.OptionsValidationEmitter();
        var driver    = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees
            .Select(t => t.ToString())
            .First();
    }
}
```

**Step 4: Add Options.Generator reference to test project**

In `tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj`, add:

```xml
<ProjectReference Include="..\..\src\ZeroAlloc.Validation.Options.Generator\ZeroAlloc.Validation.Options.Generator.csproj"
                  ReferenceOutputAssembly="true" />
```

**Step 5: Run tests to confirm they fail**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "OptionsGeneratorTests" -v minimal
```

Expected: FAIL — `OptionsValidationEmitter` does not exist.

**Step 6: Create OptionsValidationEmitter**

Create `src/ZeroAlloc.Validation.Options.Generator/OptionsValidationEmitter.cs`:

```csharp
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Validation.Inject;

namespace ZeroAlloc.Validation.Options.Generator;

[Generator]
public sealed class OptionsValidationEmitter : IIncrementalGenerator
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validateClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

#pragma warning disable EPS06
        var collected = validateClasses.Collect();
#pragma warning restore EPS06
        context.RegisterSourceOutput(collected, Emit);
    }

    private static void Emit(SourceProductionContext ctx, ImmutableArray<INamedTypeSymbol> models)
    {
        if (models.IsDefaultOrEmpty) return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine("using Microsoft.Extensions.Options;");
        sb.AppendLine();
        sb.AppendLine("public static class ZeroAllocOptionsValidationExtensions");
        sb.AppendLine("{");

        foreach (var model in models)
        {
            var modelFqn = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            sb.AppendLine($"    public static global::Microsoft.Extensions.Options.OptionsBuilder<{modelFqn}> ValidateWithZeroAlloc(");
            sb.AppendLine($"        this global::Microsoft.Extensions.Options.OptionsBuilder<{modelFqn}> builder)");
            sb.AppendLine("    {");
            ValidatorRegistrationEmitter.EmitRegistrations(sb, [model]);
            sb.AppendLine($"        builder.Services.TryAddSingleton<global::Microsoft.Extensions.Options.IValidateOptions<{modelFqn}>,");
            sb.AppendLine($"            global::ZeroAlloc.Validation.Options.ZeroAllocOptionsValidator<{modelFqn}>>();");
            sb.AppendLine("        return builder;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        ctx.AddSource("ZeroAlloc.Validation.ZeroAllocOptionsValidationExtensions.g.cs", sb.ToString());
    }
}
```

**Step 7: Run tests to confirm they pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "OptionsGeneratorTests" -v minimal
```

Expected: all 4 tests PASS.

**Step 8: Run full suite for regressions**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all tests pass.

**Step 9: Commit**

```bash
git add src/ZeroAlloc.Validation.Options.Generator/ \
        tests/ZeroAlloc.Validation.Tests/Generator/OptionsGeneratorTests.cs \
        tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj \
        ZeroAlloc.Validation.slnx
git commit -m "feat: add ZeroAlloc.Validation.Options.Generator emitting ValidateWithZeroAlloc() overloads"
```

---

### Task 6: Options integration tests

**Files:**
- Create: `tests/ZeroAlloc.Validation.Tests.Options/ZeroAlloc.Validation.Tests.Options.csproj`
- Create: `tests/ZeroAlloc.Validation.Tests.Options/OptionsIntegrationTests.cs`
- Create: `tests/ZeroAlloc.Validation.Tests.Options/TestOptionsModels.cs`
- Modify: `ZeroAlloc.Validation.slnx`

**Step 1: Create the test csproj**

Create `tests/ZeroAlloc.Validation.Tests.Options/ZeroAlloc.Validation.Tests.Options.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroAlloc.Validation\ZeroAlloc.Validation.csproj" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Validation.Options\ZeroAlloc.Validation.Options.csproj" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Validation.Generator\ZeroAlloc.Validation.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Validation.Options.Generator\ZeroAlloc.Validation.Options.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

**Step 2: Create test models**

Create `tests/ZeroAlloc.Validation.Tests.Options/TestOptionsModels.cs`:

```csharp
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

[Validate]
public class DatabaseOptions
{
    [NotEmpty]    public string ConnectionString { get; set; } = "";
    [GreaterThan(0)] public int MaxPoolSize      { get; set; }
}

[Validate]
public class SmtpOptions
{
    [NotEmpty]       public string Host { get; set; } = "";
    [GreaterThan(0)] public int    Port { get; set; }
}
```

**Step 3: Write failing integration tests**

Create `tests/ZeroAlloc.Validation.Tests.Options/OptionsIntegrationTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Options;

public class OptionsIntegrationTests
{
    [Fact]
    public void ValidateWithZeroAlloc_ValidOptions_PassesValidation()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Configure(o => { o.ConnectionString = "Server=localhost"; o.MaxPoolSize = 10; })
            .ValidateWithZeroAlloc();

        var sp     = services.BuildServiceProvider();
        var result = sp.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().CurrentValue;

        // If validation passes, accessing CurrentValue does not throw
        Assert.Equal("Server=localhost", result.ConnectionString);
    }

    [Fact]
    public void ValidateWithZeroAlloc_InvalidOptions_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Configure(o => { o.ConnectionString = ""; o.MaxPoolSize = 0; }) // both invalid
            .ValidateWithZeroAlloc()
            .ValidateOnStart();

        var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.Contains("ConnectionString", ex.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateWithZeroAlloc_RegistersValidatorForT_InDI()
    {
        var services = new ServiceCollection();
        services.AddOptions<SmtpOptions>()
            .Configure(o => { o.Host = "smtp.example.com"; o.Port = 587; })
            .ValidateWithZeroAlloc();

        var sp        = services.BuildServiceProvider();
        var validator = sp.GetService<ValidatorFor<SmtpOptions>>();

        Assert.NotNull(validator);
    }

    [Fact]
    public void ValidateWithZeroAlloc_CalledTwice_NoDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Configure(o => { o.ConnectionString = "Server=localhost"; o.MaxPoolSize = 10; })
            .ValidateWithZeroAlloc()
            .ValidateWithZeroAlloc(); // second call — TryAdd should make this a no-op

        var sp = services.BuildServiceProvider();

        // Only one IValidateOptions<DatabaseOptions> should be registered
        var validators = sp.GetServices<IValidateOptions<DatabaseOptions>>();
        Assert.Single(validators);
    }

    [Fact]
    public void TwoOptionsClasses_BothValidated_Independently()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Configure(o => { o.ConnectionString = ""; o.MaxPoolSize = 0; }) // invalid
            .ValidateWithZeroAlloc();
        services.AddOptions<SmtpOptions>()
            .Configure(o => { o.Host = "smtp.example.com"; o.Port = 587; }) // valid
            .ValidateWithZeroAlloc();

        var sp = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        // SmtpOptions is valid — no exception
        var smtp = sp.GetRequiredService<IOptions<SmtpOptions>>().Value;
        Assert.Equal("smtp.example.com", smtp.Host);
    }
}
```

**Step 4: Add to solution**

In `ZeroAlloc.Validation.slnx`, add inside the `<Folder Name="/tests/">` block:

```xml
<Project Path="tests/ZeroAlloc.Validation.Tests.Options/ZeroAlloc.Validation.Tests.Options.csproj" />
```

**Step 5: Run tests to confirm they fail**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests.Options -v minimal
```

Expected: FAIL — `ValidateWithZeroAlloc()` method not found (generator not yet wired into the
test project as an `Analyzer` item).

**Step 6: Build the test project to verify generator wiring**

```bash
dotnet build tests/ZeroAlloc.Validation.Tests.Options
```

Expected: succeeds — the generator emits `ValidateWithZeroAlloc()` overloads.

**Step 7: Run all integration tests**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests.Options -v minimal
```

Expected: all 5 tests PASS.

**Step 8: Run full test suite**

```bash
dotnet test tests/ -v minimal
```

Expected: all tests across all test projects pass.

**Step 9: Commit**

```bash
git add tests/ZeroAlloc.Validation.Tests.Options/ \
        ZeroAlloc.Validation.slnx
git commit -m "feat: add ZeroAlloc.Validation.Options integration tests"
```

---

### Task 7: Wire Inject generator into ZeroAlloc.Validation.Inject NuGet package + add to csproj

**Files:**
- Create: `src/ZeroAlloc.Validation.Inject/ZeroAlloc.Validation.Inject.Runtime.csproj` (optional — see note)

**Background:** The `ZeroAlloc.Validation.Inject` project IS the generator. For NuGet packaging,
the generator DLL must be placed in the `analyzers/dotnet/cs/` folder. This is handled by
adding the project as an `Analyzer` reference, which NuGet does automatically for `IsRoslynComponent=true`
projects. No runtime project is needed for `.Inject` — it is generator-only.

To verify the packaging works correctly, confirm the `.csproj` has `IsPackable` set appropriately
and the `PackageId` set. This was already done in Task 2. No additional work is needed.

**Step 1: Final full suite run**

```bash
dotnet test tests/ -v minimal
dotnet build src/ -v minimal
```

Expected: everything passes and builds.

**Step 2: Commit**

No new files — this is a verification task.

```bash
git commit --allow-empty -m "chore: verify full suite passes after Inject and Options implementation"
```
