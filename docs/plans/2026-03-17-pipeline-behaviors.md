# Pipeline Behaviors Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Integrate ZeroAlloc.Pipeline into ZeroAlloc.Validation so users can declare pre/post
validation behaviors (`[PipelineBehavior]`), while also refactoring the generator to use
`PipelineEmitter.EmitChain()` internally.

**Architecture:** The generator detects `[PipelineBehavior]` classes at compile time and emits
a nested static lambda chain wrapping the rule evaluation terminal. When no behaviors exist the
current direct path is emitted unchanged. Sync behaviors run in both `Validate()` and
`ValidateAsync()`; async behaviors only run in `ValidateAsync()`.

**Tech Stack:** C# 12, Roslyn incremental generators, ZeroAlloc.Pipeline 0.1.1,
ZeroAlloc.Pipeline.Generators 0.1.1, xUnit

---

### Key files to read before starting

- `src/ZeroAlloc.Validation/Core/ValidatorFor.cs` — base class being extended
- `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` — orchestrator
- `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` — emits rule bodies
- `src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs` — filter to update
- `tests/ZeroAlloc.Validation.Tests/Generator/GeneratorDiscoveryTests.cs` — test pattern to follow
- `C:/Projects/Prive/ZeroAlloc.Pipeline/src/ZeroAlloc.Pipeline.Generators/PipelineEmitter.cs`
- `C:/Projects/Prive/ZeroAlloc.Pipeline/src/ZeroAlloc.Pipeline.Generators/PipelineBehaviorDiscoverer.cs`
- `C:/Projects/Prive/ZeroAlloc.Pipeline/src/ZeroAlloc.Pipeline.Generators/PipelineShape.cs`

---

### Task 1: Add ZeroAlloc.Pipeline package references

**Files:**
- Modify: `src/ZeroAlloc.Validation/ZeroAlloc.Validation.csproj`
- Modify: `src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj`
- Modify: `tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj`

**Step 1: Add to ZeroAlloc.Validation.csproj**

```xml
<ItemGroup>
  <PackageReference Include="ZeroAlloc.Pipeline" Version="0.1.1" />
</ItemGroup>
```

**Step 2: Add to ZeroAlloc.Validation.Generator.csproj**

```xml
<ItemGroup>
  <PackageReference Include="ZeroAlloc.Pipeline.Generators" Version="0.1.1" PrivateAssets="all" />
</ItemGroup>
```

**Step 3: Add to ZeroAlloc.Validation.Tests.csproj**

```xml
<PackageReference Include="ZeroAlloc.Pipeline" Version="0.1.1" />
```

**Step 4: Verify build passes**

```bash
dotnet build src/ZeroAlloc.Validation/ZeroAlloc.Validation.csproj
dotnet build src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj
```

Expected: build succeeds with no errors.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Validation/ZeroAlloc.Validation.csproj \
        src/ZeroAlloc.Validation.Generator/ZeroAlloc.Validation.Generator.csproj \
        tests/ZeroAlloc.Validation.Tests/ZeroAlloc.Validation.Tests.csproj
git commit -m "feat: add ZeroAlloc.Pipeline package references"
```

---

### Task 2: Add ValidateAsync to ValidatorFor<T>

**Files:**
- Modify: `src/ZeroAlloc.Validation/Core/ValidatorFor.cs`
- Test: `tests/ZeroAlloc.Validation.Tests/Integration/PipelineBehaviorTests.cs` (create)

**Step 1: Write failing test**

Create `tests/ZeroAlloc.Validation.Tests/Integration/PipelineBehaviorTests.cs`:

```csharp
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

// Uses existing PersonValidator (from Person.cs which has [Validate])
public class PipelineBehaviorTests
{
    [Fact]
    public async Task ValidateAsync_NoBehaviors_ReturnsSameResultAsValidate()
    {
        var validator = new PersonValidator();
        var person = new Person { Name = "Alice", Email = "alice@example.com", Age = 30 };

        var syncResult  = validator.Validate(person);
        var asyncResult = await validator.ValidateAsync(person);

        Assert.Equal(syncResult.IsValid, asyncResult.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_NoBehaviors_InvalidModel_ReturnsSameFailures()
    {
        var validator = new PersonValidator();
        var person = new Person { Name = "", Email = "alice@example.com", Age = 30 };

        var asyncResult = await validator.ValidateAsync(person);

        Assert.False(asyncResult.IsValid);
        ValidationAssert.HasError(asyncResult, "Name");
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "PipelineBehaviorTests" -v minimal
```

Expected: FAIL — `ValidateAsync` does not exist on `ValidatorFor<T>`.

**Step 3: Add ValidateAsync to ValidatorFor<T>**

Replace `src/ZeroAlloc.Validation/Core/ValidatorFor.cs` with:

```csharp
namespace ZeroAlloc.Validation;

public abstract partial class ValidatorFor<T>
{
    public abstract ValidationResult Validate(T instance);

    // Default: wraps sync Validate in a completed ValueTask.
    // Overridden by the generator when async behaviors are present.
    public virtual global::System.Threading.Tasks.ValueTask<ValidationResult> ValidateAsync(
        T instance,
        global::System.Threading.CancellationToken ct = default)
        => global::System.Threading.Tasks.ValueTask.FromResult(Validate(instance));
}
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "PipelineBehaviorTests" -v minimal
```

Expected: PASS — both tests green.

**Step 5: Run full test suite to verify no regressions**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all tests pass.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Validation/Core/ValidatorFor.cs \
        tests/ZeroAlloc.Validation.Tests/Integration/PipelineBehaviorTests.cs
git commit -m "feat: add ValidateAsync virtual method to ValidatorFor<T>"
```

---

### Task 3: Create BehaviorDiscoverer in the generator

**Files:**
- Create: `src/ZeroAlloc.Validation.Generator/BehaviorDiscoverer.cs`
- Test: `tests/ZeroAlloc.Validation.Tests/Generator/BehaviorDiscoveryTests.cs` (create)

**Background:** `PipelineBehaviorDiscoverer.Discover(compilation)` returns `PipelineBehaviorInfo`
objects (BehaviorTypeName, Order, AppliesTo, TypeParamCount). It does NOT classify sync vs async.
`BehaviorDiscoverer` wraps it and adds that classification by inspecting the `Handle` method
return type on the re-resolved symbol.

A behavior is **async** if its `static Handle` method returns
`System.Threading.Tasks.ValueTask<TResult>` (i.e., the return type's `OriginalDefinition`
display string is `"System.Threading.Tasks.ValueTask<TResult>"`).

**Step 1: Write failing test**

Create `tests/ZeroAlloc.Validation.Tests/Generator/BehaviorDiscoveryTests.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class BehaviorDiscoveryTests
{
    [Fact]
    public void DiscoverAll_FindsSyncBehavior()
    {
        var source = """
            using ZeroAlloc.Pipeline;
            using ZeroAlloc.Validation;

            [PipelineBehavior(Order = 0)]
            public class LoggingBehavior : IPipelineBehavior
            {
                public static ValidationResult Handle<TModel>(
                    TModel instance,
                    System.Func<TModel, ValidationResult> next)
                    => next(instance);
            }
            """;

        var compilation = CreateCompilation(source);
        var (sync, async_) = BehaviorDiscoverer.DiscoverAll(compilation);

        Assert.Single(sync);
        Assert.Empty(async_);
        Assert.Contains("LoggingBehavior", sync[0].BehaviorTypeName, System.StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverAll_FindsAsyncBehavior()
    {
        var source = """
            using ZeroAlloc.Pipeline;
            using ZeroAlloc.Validation;
            using System.Threading;
            using System.Threading.Tasks;

            [PipelineBehavior(Order = 0)]
            public class CachingBehavior : IPipelineBehavior
            {
                public static async ValueTask<ValidationResult> Handle<TModel>(
                    TModel instance,
                    CancellationToken ct,
                    System.Func<TModel, CancellationToken, ValueTask<ValidationResult>> next)
                    => await next(instance, ct);
            }
            """;

        var compilation = CreateCompilation(source);
        var (sync, async_) = BehaviorDiscoverer.DiscoverAll(compilation);

        Assert.Empty(sync);
        Assert.Single(async_);
        Assert.Contains("CachingBehavior", async_[0].BehaviorTypeName, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ForModel_FiltersGlobalAndPerModel()
    {
        var globalBehavior   = new ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo("GlobalB",    0, null,                        1);
        var orderBehavior    = new ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo("OrderB",     1, "global::TestModels.Order",   1);
        var personBehavior   = new ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo("PersonB",    2, "global::TestModels.Person",  1);

        var allSync = new System.Collections.Generic.List<ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo>
            { globalBehavior, orderBehavior, personBehavior };

        var (orderSync, _) = BehaviorDiscoverer.ForModel(allSync, [], "global::TestModels.Order");

        Assert.Equal(2, orderSync.Count);  // global + order-specific
        Assert.DoesNotContain(orderSync, b => b.BehaviorTypeName == "PersonB");
    }

    private static Compilation CreateCompilation(string source)
    {
        // Same helper pattern used in GeneratorDiscoveryTests
        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            System.AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "BehaviorDiscoveryTests" -v minimal
```

Expected: FAIL — `BehaviorDiscoverer` does not exist.

**Step 3: Create BehaviorDiscoverer.cs**

Create `src/ZeroAlloc.Validation.Generator/BehaviorDiscoverer.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Pipeline.Generators;

namespace ZeroAlloc.Validation.Generator;

internal static class BehaviorDiscoverer
{
    /// <summary>
    /// Discovers all [PipelineBehavior] classes in the compilation and classifies them as
    /// sync (Handle returns ValidationResult) or async (Handle returns ValueTask&lt;ValidationResult&gt;).
    /// </summary>
    public static (List<PipelineBehaviorInfo> Sync, List<PipelineBehaviorInfo> Async)
        DiscoverAll(Compilation compilation)
    {
        var sync  = new List<PipelineBehaviorInfo>();
        var async_ = new List<PipelineBehaviorInfo>();

        foreach (var info in PipelineBehaviorDiscoverer.Discover(compilation))
        {
            // Re-resolve the symbol to inspect the Handle method return type.
            var cleanName = info.BehaviorTypeName
                .Replace("global::", string.Empty)
                .Replace("+", ".");   // handle nested types

            var symbol = compilation.GetTypeByMetadataName(cleanName);
            if (symbol is null) continue;

            if (IsAsyncBehavior(symbol))
                async_.Add(info);
            else
                sync.Add(info);
        }

        return (sync, async_);
    }

    /// <summary>
    /// Filters the full behavior lists down to those applicable for a specific model FQN,
    /// sorted by Order ascending. A null AppliesTo means global (applies to all models).
    /// </summary>
    public static (List<PipelineBehaviorInfo> Sync, List<PipelineBehaviorInfo> Async) ForModel(
        IReadOnlyList<PipelineBehaviorInfo> allSync,
        IReadOnlyList<PipelineBehaviorInfo> allAsync,
        string modelFqn)
    {
        static bool Applies(PipelineBehaviorInfo b, string fqn) =>
            b.AppliesTo is null ||
            string.Equals(b.AppliesTo, fqn, System.StringComparison.Ordinal);

        var sync  = allSync .Where(b => Applies(b, modelFqn)).OrderBy(b => b.Order).ToList();
        var async_ = allAsync.Where(b => Applies(b, modelFqn)).OrderBy(b => b.Order).ToList();

        return (sync, async_);
    }

    private static bool IsAsyncBehavior(INamedTypeSymbol symbol)
    {
        var current = symbol;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IMethodSymbol method) continue;
                if (!string.Equals(method.Name, "Handle", System.StringComparison.Ordinal)) continue;
                if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Public) continue;
                if (method.TypeParameters.Length == 0) continue;

                // ValueTask<T> return type — original definition is "System.Threading.Tasks.ValueTask<TResult>"
                if (method.ReturnType is INamedTypeSymbol rt
                    && rt.IsGenericType
                    && string.Equals(
                        rt.OriginalDefinition.ToDisplayString(),
                        "System.Threading.Tasks.ValueTask<TResult>",
                        System.StringComparison.Ordinal))
                    return true;
            }
            current = current.BaseType;
        }
        return false;
    }
}
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "BehaviorDiscoveryTests" -v minimal
```

Expected: all 3 tests PASS.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Validation.Generator/BehaviorDiscoverer.cs \
        tests/ZeroAlloc.Validation.Tests/Generator/BehaviorDiscoveryTests.cs
git commit -m "feat: add BehaviorDiscoverer to classify sync and async pipeline behaviors"
```

---

### Task 4: Wire behaviors into the incremental generator pipeline

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs`

**Background:** The generator currently calls `context.RegisterSourceOutput(validateClasses, Emit)`.
We need to also collect all behaviors from the compilation and pass them to `Emit`.

The simplest approach: combine `validateClasses` with `context.CompilationProvider`, then call
`BehaviorDiscoverer.DiscoverAll(compilation)` inside `Emit`. This is not maximally incremental
(re-runs when compilation changes) but is correct and straightforward for a first implementation.

**Step 1: Modify `ValidatorGenerator.Initialize`**

In `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs`, change `Initialize` from:

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    var validateClasses = context.SyntaxProvider
        .ForAttributeWithMetadataName(
            ValidateAttributeFqn,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

    context.RegisterSourceOutput(validateClasses, Emit);
}
```

To:

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    var validateClasses = context.SyntaxProvider
        .ForAttributeWithMetadataName(
            ValidateAttributeFqn,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

    var combined = validateClasses.Combine(context.CompilationProvider);
    context.RegisterSourceOutput(combined, static (ctx, pair) => Emit(ctx, pair.Left, pair.Right));
}
```

**Step 2: Update the `Emit` signature**

Change:

```csharp
private static void Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol)
```

To:

```csharp
private static void Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol, Compilation compilation)
```

Add at the top of `Emit`, before `ReportNestedDiagnostics`:

```csharp
var (allSync, allAsync) = BehaviorDiscoverer.DiscoverAll(compilation);
var modelFqn = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
var (syncBehaviors, asyncBehaviors) = BehaviorDiscoverer.ForModel(allSync, allAsync, modelFqn);
```

**Step 3: Run full test suite to verify no regressions**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all tests pass (no behavior changes yet — behaviors list will be empty for all existing tests).

**Step 4: Commit**

```bash
git add src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs
git commit -m "feat: wire BehaviorDiscoverer into incremental generator pipeline"
```

---

### Task 5: Emit sync behavior chain in Validate()

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Generator/BehaviorDiscoveryTests.cs`

**Background:** `PipelineEmitter.EmitChain(behaviors, shape)` returns a C# expression string to
place after `return`. `PipelineShape.InnermostBodyFactory` receives the behavior depth and returns
a string `{ ... }` block (the lambda body). The innermost body is the current rule evaluation
code, parameterized on the instance variable name.

We need `RuleEmitter` to expose `EmitValidateBodyAsString(classSymbol, paramName)` — same logic
as `EmitValidateBody` but returns a string. Then the sync shape's `InnermostBodyFactory` calls
this with `depth == 0 ? "instance" : $"r{depth}"`.

**Step 1: Add `EmitValidateBodyAsString` to RuleEmitter**

In `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`, add at the end of the class:

```csharp
/// <summary>
/// Returns the Validate method body as a string (multi-statement block WITHOUT outer braces),
/// using <paramref name="modelParamName"/> as the instance variable.
/// Suitable for embedding inside a lambda body string passed to PipelineEmitter.
/// </summary>
public static string EmitValidateBodyAsString(INamedTypeSymbol classSymbol, string modelParamName)
{
    var sb = new System.Text.StringBuilder();
    EmitValidateBody(sb, classSymbol, modelParamName);
    return sb.ToString();
}
```

**Step 2: Write generator test for sync behavior chain emission**

Add to `tests/ZeroAlloc.Validation.Tests/Generator/BehaviorDiscoveryTests.cs`:

```csharp
[Fact]
public void Generator_WithSyncBehavior_EmitsBehaviorChain_InValidate()
{
    var source = """
        using ZeroAlloc.Validation;
        using ZeroAlloc.Pipeline;

        namespace TestModels;

        [Validate]
        public class Order { [NotEmpty] public string Reference { get; set; } = ""; }

        [PipelineBehavior(Order = 0)]
        public class LoggingBehavior : IPipelineBehavior
        {
            public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                TModel instance,
                System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                => next(instance);
        }
        """;

    var result = RunGenerator(source);

    Assert.Empty(result.Diagnostics);
    var generated = result.GeneratedTrees
        .Select(t => t.ToString())
        .FirstOrDefault(s => s.Contains("OrderValidator"));

    Assert.NotNull(generated);
    Assert.Contains("LoggingBehavior.Handle", generated, System.StringComparison.Ordinal);
    Assert.DoesNotContain("ValidateAsync", generated, System.StringComparison.Ordinal); // no async override for sync-only
}
```

Use the same `RunGenerator` helper already defined in `GeneratorDiscoveryTests.cs` (copy or move to a shared helper).

**Step 3: Run test to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "Generator_WithSyncBehavior" -v minimal
```

Expected: FAIL — generated code does not yet contain `LoggingBehavior.Handle`.

**Step 4: Emit sync chain in ValidatorGenerator**

In `ValidatorGenerator.cs`, change the `EmitFieldsAndConstructor` + rule emission section inside `Emit()`. Replace the current:

```csharp
sb.AppendLine($"    public override global::ZeroAlloc.Validation.ValidationResult Validate({modelName} instance)");
sb.AppendLine("    {");
RuleEmitter.EmitValidateBody(sb, classSymbol);
sb.AppendLine("    }");
```

With:

```csharp
sb.AppendLine($"    public override global::ZeroAlloc.Validation.ValidationResult Validate({modelName} instance)");
sb.AppendLine("    {");
if (syncBehaviors.Count == 0)
{
    // No behaviors — direct path, zero overhead (existing behavior)
    RuleEmitter.EmitValidateBody(sb, classSymbol);
}
else
{
    // Sync pipeline chain
    var fullyQualifiedModel = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var syncShape = new global::ZeroAlloc.Pipeline.Generators.PipelineShape
    {
        TypeArguments           = new[] { fullyQualifiedModel },
        OuterParameterNames     = new[] { "instance" },
        LambdaParameterPrefixes = new[] { "r" },
        InnermostBodyFactory    = depth =>
        {
            var paramName = depth == 0 ? "instance" : $"r{depth}";
            return "{\n"
                + RuleEmitter.EmitValidateBodyAsString(classSymbol, paramName)
                + "        }";
        }
    };
    var chain = global::ZeroAlloc.Pipeline.Generators.PipelineEmitter.EmitChain(syncBehaviors, syncShape);
    sb.AppendLine($"        return {chain};");
}
sb.AppendLine("    }");
```

**Step 5: Run tests to verify they pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "Generator_WithSyncBehavior" -v minimal
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: both the new test and the full suite pass.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Validation.Generator/RuleEmitter.cs \
        src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs \
        tests/ZeroAlloc.Validation.Tests/Generator/BehaviorDiscoveryTests.cs
git commit -m "feat: emit sync pipeline behavior chain in generated Validate()"
```

---

### Task 6: Emit async ValidateAsync override when async behaviors present

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs`
- Modify: `tests/ZeroAlloc.Validation.Tests/Integration/PipelineBehaviorTests.cs`

**Background:** When `asyncBehaviors.Count > 0`, emit a `ValidateAsync` override. The innermost
lambda calls `Validate(r{depth})` (the sync path including any sync behaviors) and wraps it in
`ValueTask.FromResult`. The async shape uses two lambda parameter prefixes: `r` (instance) and
`c` (CancellationToken).

**Step 1: Write integration test for async behavior**

Add to `tests/ZeroAlloc.Validation.Tests/Integration/PipelineBehaviorTests.cs`:

```csharp
[Fact]
public async Task ValidateAsync_AsyncBehavior_RunsPreAndPost()
{
    var order = new Order { Reference = "REF-001", Amount = 10, Email = "test@test.com" };
    var validator = new OrderValidator();

    var callLog = new System.Collections.Generic.List<string>();
    // We cannot inject state into a static behavior from a test.
    // Instead, test via the result and observable side effects through a test-scoped behavior.
    // This test validates that ValidateAsync executes and returns correct result.
    var result = await validator.ValidateAsync(order);
    Assert.True(result.IsValid);
}
```

**Note:** Integration-testing the pre/post hooks of a static behavior directly from a unit test
requires a behavior that records side effects in a static/thread-local field. A simpler behavioral
test verifies that the result is still correct:

Add a test model with an async behavior to the test project. Create:

`tests/ZeroAlloc.Validation.Tests/Integration/PipelineOrder.cs`:
```csharp
using ZeroAlloc.Validation;
using ZeroAlloc.Pipeline;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class PipelineOrder
{
    [NotEmpty] public string Reference { get; set; } = "";
}

[PipelineBehavior(Order = 0, AppliesTo = typeof(PipelineOrder))]
public class AsyncAuditBehavior : IPipelineBehavior
{
    // Thread-local log for test observability
    [System.ThreadStatic]
    public static System.Collections.Generic.List<string>? CallLog;

    public static async ValueTask<ValidationResult> Handle<TModel>(
        TModel instance,
        CancellationToken ct,
        System.Func<TModel, CancellationToken, ValueTask<ValidationResult>> next)
    {
        CallLog?.Add("pre");
        var result = await next(instance, ct);
        CallLog?.Add("post");
        return result;
    }
}
```

Add tests:

```csharp
[Fact]
public async Task ValidateAsync_AsyncBehavior_RunsPreAndPostHooks()
{
    AsyncAuditBehavior.CallLog = new System.Collections.Generic.List<string>();
    var validator = new PipelineOrderValidator();
    var order = new PipelineOrder { Reference = "REF-001" };

    var result = await validator.ValidateAsync(order);

    Assert.True(result.IsValid);
    Assert.Equal(new[] { "pre", "post" }, AsyncAuditBehavior.CallLog);
}

[Fact]
public async Task ValidateAsync_AsyncBehavior_CanShortCircuit()
{
    var validator = new PipelineOrderValidator();
    var order = new PipelineOrder { Reference = "" }; // invalid

    var result = await validator.ValidateAsync(order);

    Assert.False(result.IsValid);
    ValidationAssert.HasError(result, "Reference");
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "AsyncBehavior" -v minimal
```

Expected: FAIL — `ValidateAsync` override is not yet emitted.

**Step 3: Emit ValidateAsync override in ValidatorGenerator**

In `ValidatorGenerator.Emit()`, after the closing `}` of the `Validate` method, add:

```csharp
if (asyncBehaviors.Count > 0)
{
    var fullyQualifiedModel = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var asyncShape = new global::ZeroAlloc.Pipeline.Generators.PipelineShape
    {
        TypeArguments           = new[] { fullyQualifiedModel },
        OuterParameterNames     = new[] { "instance", "ct" },
        LambdaParameterPrefixes = new[] { "r", "c" },
        InnermostBodyFactory    = depth =>
        {
            var paramName = depth == 0 ? "instance" : $"r{depth}";
            return $"global::System.Threading.Tasks.ValueTask.FromResult(Validate({paramName}))";
        }
    };
    var chain = global::ZeroAlloc.Pipeline.Generators.PipelineEmitter.EmitChain(asyncBehaviors, asyncShape);
    sb.AppendLine();
    sb.AppendLine($"    public override global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult> ValidateAsync({modelName} instance, global::System.Threading.CancellationToken ct = default)");
    sb.AppendLine($"        => {chain};");
}
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "AsyncBehavior" -v minimal
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all tests pass.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs \
        tests/ZeroAlloc.Validation.Tests/Integration/PipelineBehaviorTests.cs \
        tests/ZeroAlloc.Validation.Tests/Integration/PipelineOrder.cs
git commit -m "feat: emit async ValidateAsync override when async behaviors present"
```

---

### Task 7: ZV0015 diagnostic — duplicate Order values

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs`
- Test: `tests/ZeroAlloc.Validation.Tests/Generator/BehaviorDiscoveryTests.cs`

**Background:** Two behaviors with the same `Order` value for the same model is ambiguous.
Report `ZV0015` (Error) when this is detected. Use `Location.None` since we have the FQN but
not the source location of the `[PipelineBehavior]` attribute at this point.

**Step 1: Write test for ZV0015**

Add to `BehaviorDiscoveryTests.cs`:

```csharp
[Fact]
public void Generator_DuplicateBehaviorOrder_EmitsZV0015()
{
    var source = """
        using ZeroAlloc.Validation;
        using ZeroAlloc.Pipeline;

        namespace TestModels;

        [Validate]
        public class Order { [NotEmpty] public string Reference { get; set; } = ""; }

        [PipelineBehavior(Order = 0)]
        public class BehaviorA : IPipelineBehavior
        {
            public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                => next(inst);
        }

        [PipelineBehavior(Order = 0)]  // same Order as BehaviorA — conflict
        public class BehaviorB : IPipelineBehavior
        {
            public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                TModel inst, System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                => next(inst);
        }
        """;

    var result = RunGenerator(source);

    Assert.Contains(result.Diagnostics, d => d.Id == "ZV0015");
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "DuplicateBehaviorOrder" -v minimal
```

Expected: FAIL — ZV0015 is not yet emitted.

**Step 3: Add ZV0015 descriptor and reporting to ValidatorGenerator**

In `ValidatorGenerator.cs`, add the descriptor alongside ZV0011/ZV0012/ZV0013:

```csharp
private static readonly DiagnosticDescriptor ZV0015 = new DiagnosticDescriptor(
    id: "ZV0015",
    title: "Duplicate pipeline behavior Order",
    messageFormat: "Two behaviors have the same Order value {0} for model '{1}'. Each behavior must have a unique Order.",
    category: "ZeroAlloc.Validation",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

In `Emit()`, after computing `(syncBehaviors, asyncBehaviors)`, add:

```csharp
ReportDuplicateOrderDiagnostics(ctx, syncBehaviors, asyncBehaviors, classSymbol.Name);
```

Add the helper method:

```csharp
private static void ReportDuplicateOrderDiagnostics(
    SourceProductionContext ctx,
    System.Collections.Generic.List<PipelineBehaviorInfo> sync,
    System.Collections.Generic.List<PipelineBehaviorInfo> async_,
    string modelName)
{
    var all = new System.Collections.Generic.List<PipelineBehaviorInfo>(sync.Count + async_.Count);
    all.AddRange(sync);
    all.AddRange(async_);

    var seen = new System.Collections.Generic.Dictionary<int, string>();
    foreach (var b in all)
    {
        if (seen.TryGetValue(b.Order, out var first))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                ZV0015,
                Location.None,
                b.Order,
                modelName));
        }
        else
        {
            seen[b.Order] = b.BehaviorTypeName;
        }
    }
}
```

**Step 4: Run tests**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "DuplicateBehaviorOrder" -v minimal
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all pass.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs \
        tests/ZeroAlloc.Validation.Tests/Generator/BehaviorDiscoveryTests.cs
git commit -m "feat: add ZV0015 diagnostic for duplicate pipeline behavior Order values"
```

---

### Task 8: Update AspNetCore filter to use ValidateAsync

**Files:**
- Modify: `src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs`
- Test: `tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs`

**Background:** The current filter implements `IActionFilter` (sync) and calls `.Validate()`.
Since the filter runs in an async MVC context, it should become `IAsyncActionFilter` calling
`ValidateAsync()` so that async behaviors run on every HTTP request automatically.

`IAsyncActionFilter` has a single method:
```csharp
Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next);
```

The filter validates each argument, short-circuits on failure (returns 422), or calls `next()`.

**Step 1: Read the existing AspNetCore generator test to understand the test pattern**

Read `tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs` fully before coding.

**Step 2: Add test asserting the filter uses IAsyncActionFilter**

In `AspNetCoreGeneratorTests.cs`, add:

```csharp
[Fact]
public void GeneratedFilter_ImplementsIAsyncActionFilter()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;
        [Validate]
        public class CreateOrderRequest { [NotEmpty] public string Reference { get; set; } = ""; }
        """;

    var generated = RunFilterGenerator(source);
    Assert.Contains("IAsyncActionFilter", generated, System.StringComparison.Ordinal);
    Assert.Contains("OnActionExecutionAsync", generated, System.StringComparison.Ordinal);
    Assert.Contains("ValidateAsync", generated, System.StringComparison.Ordinal);
}
```

**Step 3: Run test to verify it fails**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "ImplementsIAsyncActionFilter" -v minimal
```

Expected: FAIL.

**Step 4: Update AspNetCoreFilterEmitter.AppendFilterHeader and AppendDispatchSwitch**

In `AspNetCoreFilterEmitter.cs`, change `AppendFilterHeader` to emit `IAsyncActionFilter`:

Replace the class declaration and method:
```csharp
// Old:
sb.AppendLine("internal sealed class ZValidationActionFilter : global::Microsoft.AspNetCore.Mvc.Filters.IActionFilter");
// ...
sb.AppendLine("    public void OnActionExecuting(...)");

// New:
sb.AppendLine("internal sealed class ZValidationActionFilter : global::Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter");
// ...
sb.AppendLine("    public async global::System.Threading.Tasks.Task OnActionExecutionAsync(");
sb.AppendLine("        global::Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context,");
sb.AppendLine("        global::Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)");
sb.AppendLine("    {");
sb.AppendLine("        foreach (var arg in context.ActionArguments.Values)");
sb.AppendLine("        {");
sb.AppendLine("            var result = await DispatchAsync(arg);");
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
sb.AppendLine("        await next();");
sb.AppendLine("    }");
sb.AppendLine();
```

In `AppendDispatchSwitch`, rename `Dispatch` to `DispatchAsync` and change:

```csharp
// Old: private global::ZeroAlloc.Validation.ValidationResult? Dispatch(object? arg) => arg switch
// New:
sb.AppendLine("    private global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult?> DispatchAsync(object? arg) => arg switch");
```

And for each model:
```csharp
// Old: .Validate({varName}),
// New: new global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult?>(_services.GetRequiredService<{validatorName}>().ValidateAsync({varName}).Preserve()),
```

Wait — `ValueTask` switch expressions are tricky. A cleaner approach: make `DispatchAsync` an
actual `async Task` method instead of a switch expression:

```csharp
sb.AppendLine("    private async global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult?> DispatchAsync(object? arg)");
sb.AppendLine("    {");
sb.AppendLine("        switch (arg)");
sb.AppendLine("        {");
foreach (var model in models)
{
    sb.AppendLine($"            case {fullName} {varName}:");
    sb.AppendLine($"                return await _services.GetRequiredService<{validatorName}>().ValidateAsync({varName});");
}
sb.AppendLine("            default: return null;");
sb.AppendLine("        }");
sb.AppendLine("    }");
```

**Step 5: Run tests**

```bash
dotnet test tests/ZeroAlloc.Validation.Tests --filter "AspNetCore" -v minimal
dotnet test tests/ZeroAlloc.Validation.Tests.AspNetCore -v minimal
dotnet test tests/ZeroAlloc.Validation.Tests -v minimal
```

Expected: all pass.

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Validation.AspNetCore.Generator/AspNetCoreFilterEmitter.cs \
        tests/ZeroAlloc.Validation.Tests/Generator/AspNetCoreGeneratorTests.cs
git commit -m "feat: update ZValidationActionFilter to IAsyncActionFilter using ValidateAsync"
```

---

### Task 9: Update docs/diagnostics.md with ZV0015

**Files:**
- Modify: `docs/diagnostics.md`

**Step 1: Read current docs/diagnostics.md**

**Step 2: Add ZV0015 entry**

Add a row to the diagnostics table and a section for ZV0015 matching the existing format of ZV0011/ZV0012/ZV0013. Content:

| Field | Value |
|---|---|
| ID | ZV0015 |
| Severity | Error |
| Title | Duplicate pipeline behavior Order |
| When fired | Two `[PipelineBehavior]` classes targeting the same model have the same `Order` value |
| Fix | Assign unique `Order` values to each behavior |

**Step 3: Commit**

```bash
git add docs/diagnostics.md
git commit -m "docs: add ZV0015 diagnostic to diagnostics reference"
```
