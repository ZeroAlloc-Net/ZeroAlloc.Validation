# ASP.NET Core Integration + DI Lifetime Forwarding Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Forward ZeroAlloc.Inject lifetime attributes from models to generated validators, and emit a source-generated ASP.NET Core action filter that auto-validates all `[Validate]` models.

**Architecture:** Two independent additions: (1) core `ValidatorGenerator.cs` checks the model for `[Transient]`/`[Scoped]`/`[Singleton]` by FQN string and mirrors the attribute onto the generated validator class; (2) new `ZValidation.AspNetCore.Generator` project collects every `[Validate]` model in the compilation and emits a type-switch `ZValidationActionFilter` plus `AddZValidationAutoValidation` extension method — no reflection, fully AOT-safe.

**Tech Stack:** Roslyn `IIncrementalGenerator` (netstandard2.0), C# 12 pattern-matching switch expressions, `Microsoft.AspNetCore.Mvc.Testing` for integration tests, xunit.

---

## Task 1: DI lifetime forwarding in `ValidatorGenerator.cs`

**Files:**
- Modify: `src/ZValidation.Generator/ValidatorGenerator.cs`
- Test: `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`

### Step 1: Write four failing tests

Add to the bottom of `GeneratorRuleEmissionTests.cs` (before `RunGeneratorGetSource`):

```csharp
[Fact]
public void Generator_ForwardsScoped_ToValidator()
{
    var source = """
        using ZValidation;
        namespace ZeroAlloc.Inject { public sealed class ScopedAttribute : System.Attribute {} }
        namespace TestModels;
        [Validate, global::ZeroAlloc.Inject.Scoped]
        public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("[global::ZeroAlloc.Inject.ScopedAttribute]", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_ForwardsTransient_ToValidator()
{
    var source = """
        using ZValidation;
        namespace ZeroAlloc.Inject { public sealed class TransientAttribute : System.Attribute {} }
        namespace TestModels;
        [Validate, global::ZeroAlloc.Inject.Transient]
        public class Order { [NotEmpty] public string Ref { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("[global::ZeroAlloc.Inject.TransientAttribute]", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_ForwardsSingleton_ToValidator()
{
    var source = """
        using ZValidation;
        namespace ZeroAlloc.Inject { public sealed class SingletonAttribute : System.Attribute {} }
        namespace TestModels;
        [Validate, global::ZeroAlloc.Inject.Singleton]
        public class Country { [NotEmpty] public string Code { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.Contains("[global::ZeroAlloc.Inject.SingletonAttribute]", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_NoLifetime_EmitsNoLifetimeAttribute()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Plain { [NotEmpty] public string Value { get; set; } = ""; }
        """;

    var generated = RunGeneratorGetSource(source);
    Assert.DoesNotContain("ZeroAlloc.Inject", generated, StringComparison.Ordinal);
}
```

### Step 2: Run tests to verify they fail

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "Generator_Forwards" -v minimal
```
Expected: 3 tests fail (no `ZeroAlloc.Inject` text in output), 1 test passes (NoLifetime already works).

### Step 3: Implement lifetime forwarding in `ValidatorGenerator.cs`

Add three constants after `ValidateAttributeFqn`:

```csharp
private const string TransientFqn = "ZeroAlloc.Inject.TransientAttribute";
private const string ScopedFqn    = "ZeroAlloc.Inject.ScopedAttribute";
private const string SingletonFqn = "ZeroAlloc.Inject.SingletonAttribute";
```

In the `Emit` method, just before the line that appends `public sealed partial class ...`, add:

```csharp
var lifetimeFqn = classSymbol.GetAttributes()
    .Select(a => a.AttributeClass?.ToDisplayString())
    .FirstOrDefault(fqn => fqn is TransientFqn or ScopedFqn or SingletonFqn);

if (lifetimeFqn is not null)
    sb.AppendLine($"[global::{lifetimeFqn}]");
```

The existing `sb.AppendLine($"public sealed partial class {validatorName} : ValidatorFor<{modelName}>");` line is unchanged.

### Step 4: Run tests to verify they pass

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "Generator_Forwards|Generator_NoLifetime" -v minimal
```
Expected: all 4 tests pass.

### Step 5: Run full test suite

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```
Expected: all 130 tests pass.

### Step 6: Commit

```bash
git add src/ZValidation.Generator/ValidatorGenerator.cs
git add tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "feat: forward ZeroAlloc.Inject lifetime attributes from model to generated validator"
```

---

## Task 2: Create `ZValidation.AspNetCore.Generator` project

**Files:**
- Create: `src/ZValidation.AspNetCore.Generator/ZValidation.AspNetCore.Generator.csproj`
- Create: `src/ZValidation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs`
- Create: `tests/ZValidation.Tests/Generator/AspNetCoreGeneratorTests.cs`
- Modify: `ZValidation.slnx`

### Step 1: Write failing tests

Create `tests/ZValidation.Tests/Generator/AspNetCoreGeneratorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZValidation;

namespace ZValidation.Tests.Generator;

public class AspNetCoreGeneratorTests
{
    [Fact]
    public void Generator_EmitsDispatch_ForBothValidateModels()
    {
        var source = """
            using ZValidation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            [Validate] public class Order    { [NotEmpty] public string Ref  { get; set; } = ""; }
            """;

        var filter = RunAspNetGeneratorGetSource(source, "ZValidationActionFilter.g.cs");
        Assert.Contains("global::MyApp.Customer", filter, StringComparison.Ordinal);
        Assert.Contains("global::MyApp.Order",    filter, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsExtensionMethod_WithTryAddTransient_ForBothValidators()
    {
        var source = """
            using ZValidation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            [Validate] public class Order    { [NotEmpty] public string Ref  { get; set; } = ""; }
            """;

        var ext = RunAspNetGeneratorGetSource(source, "ZValidationServiceCollectionExtensions.g.cs");
        Assert.Contains("TryAddTransient<global::MyApp.CustomerValidator>", ext, StringComparison.Ordinal);
        Assert.Contains("TryAddTransient<global::MyApp.OrderValidator>",    ext, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NonValidateType_NotPresentInDispatch()
    {
        var source = """
            using ZValidation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            public class NotAModel { public string X { get; set; } = ""; }
            """;

        var filter = RunAspNetGeneratorGetSource(source, "ZValidationActionFilter.g.cs");
        Assert.DoesNotContain("NotAModel", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsAddZValidationAutoValidation_ExtensionMethod()
    {
        var source = """
            using ZValidation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var ext = RunAspNetGeneratorGetSource(source, "ZValidationServiceCollectionExtensions.g.cs");
        Assert.Contains("AddZValidationAutoValidation", ext, StringComparison.Ordinal);
    }

    private static string RunAspNetGeneratorGetSource(string source, string fileName)
    {
        var sources = RunAspNetGeneratorGetSources(source);
        return sources.First(s => s.Contains(fileName.Replace(".g.cs", string.Empty), StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> RunAspNetGeneratorGetSources(string source)
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

        var generator = new ZValidation.AspNetCore.Generator.AspNetCoreFilterEmitter();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
    }
}
```

**Note:** This test will not compile until the generator project exists — that's expected. The test project already references `ZValidation.Generator` as a compile-time analyzer. Add a reference to the new generator using the same mechanism (see Step 3 and Task 3).

For now, just create the file. The build will fail until Steps 2–4 are done.

### Step 2: Create the generator project file

Create `src/ZValidation.AspNetCore.Generator/ZValidation.AspNetCore.Generator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="5.3.0" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

### Step 3: Create `AspNetCoreFilterEmitter.cs`

Create `src/ZValidation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs`:

```csharp
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZValidation.AspNetCore.Generator;

[Generator]
public sealed class AspNetCoreFilterEmitter : IIncrementalGenerator
{
    private const string ValidateAttributeFqn = "ZValidation.ValidateAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validateClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        context.RegisterSourceOutput(validateClasses, EmitFiles);
    }

    private static void EmitFiles(SourceProductionContext ctx, ImmutableArray<INamedTypeSymbol> models)
    {
        if (models.IsDefaultOrEmpty) return;

        ctx.AddSource("ZValidationActionFilter.g.cs",         EmitFilter(models));
        ctx.AddSource("ZValidationServiceCollectionExtensions.g.cs", EmitExtensions(models));
    }

    private static string EmitFilter(ImmutableArray<INamedTypeSymbol> models)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine("using Microsoft.AspNetCore.Mvc.Filters;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("internal sealed class ZValidationActionFilter : global::Microsoft.AspNetCore.Mvc.Filters.IActionFilter");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly global::System.IServiceProvider _services;");
        sb.AppendLine();
        sb.AppendLine("    public ZValidationActionFilter(global::System.IServiceProvider services) => _services = services;");
        sb.AppendLine();
        sb.AppendLine("    public void OnActionExecuting(global::Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)");
        sb.AppendLine("    {");
        sb.AppendLine("        foreach (var arg in context.ActionArguments.Values)");
        sb.AppendLine("        {");
        sb.AppendLine("            var result = Dispatch(arg);");
        sb.AppendLine("            if (result is null || result.Value.IsValid) continue;");
        sb.AppendLine();
        sb.AppendLine("            var pd = new global::Microsoft.AspNetCore.Mvc.ValidationProblemDetails();");
        sb.AppendLine("            foreach (ref readonly var f in result.Value.Failures)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (!pd.Errors.ContainsKey(f.PropertyName))");
        sb.AppendLine("                    pd.Errors[f.PropertyName] = global::System.Array.Empty<string>();");
        sb.AppendLine("                pd.Errors[f.PropertyName] = [.. pd.Errors[f.PropertyName], f.ErrorMessage];");
        sb.AppendLine("            }");
        sb.AppendLine("            context.Result = new global::Microsoft.AspNetCore.Mvc.UnprocessableEntityObjectResult(pd);");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public void OnActionExecuted(global::Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext context) { }");
        sb.AppendLine();
        sb.AppendLine("    private global::ZValidation.ValidationResult? Dispatch(object? arg) => arg switch");
        sb.AppendLine("    {");

        foreach (var model in models)
        {
            var fullName  = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var validatorName = model.ContainingNamespace.IsGlobalNamespace
                ? $"global::{model.Name}Validator"
                : $"global::{model.ContainingNamespace.ToDisplayString()}.{model.Name}Validator";
            var varName = char.ToLowerInvariant(model.Name[0]) + model.Name.Substring(1);
            sb.AppendLine($"        {fullName} {varName} => _services.GetRequiredService<{validatorName}>().Validate({varName}),");
        }

        sb.AppendLine("        _ => null");
        sb.AppendLine("    };");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EmitExtensions(ImmutableArray<INamedTypeSymbol> models)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine();
        sb.AppendLine("public static class ZValidationServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZValidationAutoValidation(");
        sb.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var model in models)
        {
            var validatorName = model.ContainingNamespace.IsGlobalNamespace
                ? $"global::{model.Name}Validator"
                : $"global::{model.ContainingNamespace.ToDisplayString()}.{model.Name}Validator";
            sb.AppendLine($"        services.TryAddTransient<{validatorName}>();");
        }

        sb.AppendLine("        services.TryAddTransient<ZValidationActionFilter>();");
        sb.AppendLine("        services.Configure<global::Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.Add<ZValidationActionFilter>());");
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

### Step 4: Add the new generator to `ZValidation.Tests.csproj`

In `tests/ZValidation.Tests/ZValidation.Tests.csproj`, add inside the existing `<ItemGroup>` that has `ZValidation.Generator`:

```xml
<ProjectReference Include="..\..\src\ZValidation.AspNetCore.Generator\ZValidation.AspNetCore.Generator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="true" />
```

(`ReferenceOutputAssembly="true"` so the test class can reference `AspNetCoreFilterEmitter` directly.)

### Step 5: Add new projects to `ZValidation.slnx`

In `ZValidation.slnx`, add inside `<Folder Name="/src/">`:

```xml
<Project Path="src/ZValidation.AspNetCore.Generator/ZValidation.AspNetCore.Generator.csproj" />
```

### Step 6: Build and run the new generator tests

```
dotnet build src/ZValidation.AspNetCore.Generator/ZValidation.AspNetCore.Generator.csproj
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "AspNetCoreGenerator" -v minimal
```
Expected: all 4 generator tests pass.

### Step 7: Run the full test suite

```
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```
Expected: all tests pass (no regressions).

### Step 8: Commit

```bash
git add src/ZValidation.AspNetCore.Generator/
git add tests/ZValidation.Tests/Generator/AspNetCoreGeneratorTests.cs
git add tests/ZValidation.Tests/ZValidation.Tests.csproj
git add ZValidation.slnx
git commit -m "feat: add ZValidation.AspNetCore.Generator emitting action filter and DI extension method"
```

---

## Task 3: Wire `ZValidation.AspNetCore` to the new generator

**Files:**
- Modify: `src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj`
- Delete: `src/ZValidation.AspNetCore/Integration/ServiceCollectionExtensions.cs`

### Step 1: Add generator reference to `ZValidation.AspNetCore.csproj`

In `src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj`, add a new `<ItemGroup>` after the existing one:

```xml
<ItemGroup>
  <ProjectReference Include="..\ZValidation.AspNetCore.Generator\ZValidation.AspNetCore.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

### Step 2: Delete the empty stub

Delete `src/ZValidation.AspNetCore/Integration/ServiceCollectionExtensions.cs`. The generated `ZValidationServiceCollectionExtensions.g.cs` replaces it.

### Step 3: Build to confirm generator is wired

```
dotnet build src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj
```
Expected: builds successfully. No source for `[Validate]` models exists in this library itself (the consumer's project has those), so the emitted filter will be empty — that is correct behaviour.

### Step 4: Commit

```bash
git add src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj
git rm src/ZValidation.AspNetCore/Integration/ServiceCollectionExtensions.cs
git commit -m "feat: wire ZValidation.AspNetCore.Generator as analyzer into ZValidation.AspNetCore"
```

---

## Task 4: Integration tests

**Files:**
- Create: `tests/ZValidation.Tests.AspNetCore/ZValidation.Tests.AspNetCore.csproj`
- Create: `tests/ZValidation.Tests.AspNetCore/SampleModel.cs`
- Create: `tests/ZValidation.Tests.AspNetCore/SampleController.cs`
- Create: `tests/ZValidation.Tests.AspNetCore/TestApp.cs`
- Create: `tests/ZValidation.Tests.AspNetCore/AutoValidationIntegrationTests.cs`
- Modify: `ZValidation.slnx`

### Step 1: Create the project file

Create `tests/ZValidation.Tests.AspNetCore/ZValidation.Tests.AspNetCore.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" Condition="'$(TargetFramework)' == 'net8.0'" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" Condition="'$(TargetFramework)' == 'net9.0'" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" Condition="'$(TargetFramework)' == 'net10.0'" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ZValidation\ZValidation.csproj" />
    <ProjectReference Include="..\..\src\ZValidation.AspNetCore\ZValidation.AspNetCore.csproj" />
    <ProjectReference Include="..\..\src\ZValidation.Generator\ZValidation.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\..\src\ZValidation.AspNetCore.Generator\ZValidation.AspNetCore.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

### Step 2: Create the test model

Create `tests/ZValidation.Tests.AspNetCore/SampleModel.cs`:

```csharp
using ZValidation;

namespace ZValidation.Tests.AspNetCore;

[Validate]
public partial class SampleModel
{
    [NotEmpty] public string Name { get; set; } = "";
    [GreaterThan(0)] public int Quantity { get; set; }
}
```

### Step 3: Create the test controller

Create `tests/ZValidation.Tests.AspNetCore/SampleController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ZValidation.Tests.AspNetCore;

[ApiController]
[Route("sample")]
public class SampleController : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] SampleModel model) =>
        Ok(new { model.Name, model.Quantity });

    [HttpPost("unknown")]
    public IActionResult PostUnknown([FromBody] string raw) => Ok(raw);
}
```

### Step 4: Create the test web application

Create `tests/ZValidation.Tests.AspNetCore/TestApp.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ZValidation.Tests.AspNetCore;

public class TestApp
{
    public static WebApplication Build()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(SampleController).Assembly);

        builder.Services.AddZValidationAutoValidation();

        var app = builder.Build();
        app.MapControllers();
        return app;
    }
}
```

### Step 5: Write the integration tests

Create `tests/ZValidation.Tests.AspNetCore/AutoValidationIntegrationTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ZValidation.Tests.AspNetCore;

public class AutoValidationIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _app = TestApp.Build();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public async Task ValidModel_Returns200()
    {
        var response = await _client!.PostAsJsonAsync("/sample", new { Name = "Widget", Quantity = 5 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidModel_EmptyName_Returns422()
    {
        var response = await _client!.PostAsJsonAsync("/sample", new { Name = "", Quantity = 5 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidModel_NegativeQuantity_Returns422()
    {
        var response = await _client!.PostAsJsonAsync("/sample", new { Name = "Widget", Quantity = 0 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Quantity", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownModelType_FilterSkips_Returns200()
    {
        using var content = new StringContent("\"hello\"", System.Text.Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/sample/unknown", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddZValidationAutoValidation_RegistersFilter()
    {
        var filter = _app!.Services.GetService<ZValidationActionFilter>();
        Assert.NotNull(filter);
    }
}
```

### Step 6: Add to `ZValidation.slnx`

In `ZValidation.slnx`, add inside `<Folder Name="/tests/">`:

```xml
<Project Path="tests/ZValidation.Tests.AspNetCore/ZValidation.Tests.AspNetCore.csproj" />
```

### Step 7: Build and run the integration tests

```
dotnet build tests/ZValidation.Tests.AspNetCore/ZValidation.Tests.AspNetCore.csproj
dotnet test tests/ZValidation.Tests.AspNetCore/ZValidation.Tests.AspNetCore.csproj -v minimal
```
Expected: all 5 tests pass across all 3 target frameworks.

### Step 8: Run the full solution build

```
dotnet build ZValidation.slnx
```
Expected: zero warnings, zero errors.

### Step 9: Commit

```bash
git add tests/ZValidation.Tests.AspNetCore/
git add ZValidation.slnx
git commit -m "feat: add ASP.NET Core auto-validation integration tests"
```

---

## Summary of changes

| File | Action |
|------|--------|
| `src/ZValidation.Generator/ValidatorGenerator.cs` | Add lifetime FQN constants + emit attribute on validator |
| `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs` | Add 4 lifetime forwarding tests |
| `src/ZValidation.AspNetCore.Generator/ZValidation.AspNetCore.Generator.csproj` | New project |
| `src/ZValidation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs` | New generator |
| `tests/ZValidation.Tests/Generator/AspNetCoreGeneratorTests.cs` | 4 generator output tests |
| `tests/ZValidation.Tests/ZValidation.Tests.csproj` | Add generator reference |
| `src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj` | Add generator analyzer reference |
| `src/ZValidation.AspNetCore/Integration/ServiceCollectionExtensions.cs` | **Delete** (replaced by generated code) |
| `tests/ZValidation.Tests.AspNetCore/` | New integration test project (5 files) |
| `ZValidation.slnx` | Add 2 new projects |
