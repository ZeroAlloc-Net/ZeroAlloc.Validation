# Solution Structure Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Scaffold the ZValidation solution with all projects, references, multi-targeting, and analyzer wiring in place — ready for feature implementation.

**Architecture:** Flat `src/` + `tests/` layout. `ZValidation.Generator` (netstandard2.0) is a Roslyn incremental source generator bundled into the `ZValidation` NuGet as an analyzer. All consumer-facing projects multi-target `net8.0;net9.0;net10.0`. `ZValidation.Testing` has no test framework dependency.

**Tech Stack:** .NET 10 SDK, C# 13, Roslyn incremental source generators, xUnit 2, NUnit 4, MSTest 3, ZInject (source-gen DI).

---

## Reference: Approved Design

See [docs/plans/2026-03-15-solution-structure-design.md](2026-03-15-solution-structure-design.md) for the full design rationale.

---

### Task 1: Create the solution file and folder structure

**Files:**
- Create: `ZValidation.sln`
- Create: `src/` (empty folder placeholder)
- Create: `tests/` (empty folder placeholder)

**Step 1: Create the solution**

```bash
cd /c/Projects/Prive/ZValidation
dotnet new sln -n ZValidation
```

Expected: `ZValidation.sln` created.

**Step 2: Create top-level folders**

```bash
mkdir -p src tests docs/plans
```

**Step 3: Commit**

```bash
git init
git add ZValidation.sln docs/
git commit -m "chore: init solution"
```

---

### Task 2: Create `ZValidation.Generator` project

This must be created first because `ZValidation` (core) will reference it.

**Files:**
- Create: `src/ZValidation.Generator/ZValidation.Generator.csproj`
- Create: `src/ZValidation.Generator/ValidatorGenerator.cs`

**Step 1: Scaffold the project**

```bash
dotnet new classlib -n ZValidation.Generator -o src/ZValidation.Generator --framework netstandard2.0
rm src/ZValidation.Generator/Class1.cs
```

**Step 2: Replace the csproj with generator-specific setup**

Replace `src/ZValidation.Generator/ZValidation.Generator.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

**Step 3: Create the generator entry point**

Create `src/ZValidation.Generator/ValidatorGenerator.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace ZValidation.Generator;

[Generator]
public sealed class ValidatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Implementation will be added in subsequent tasks.
    }
}
```

**Step 4: Add to solution**

```bash
dotnet sln add src/ZValidation.Generator/ZValidation.Generator.csproj
```

**Step 5: Build to verify**

```bash
dotnet build src/ZValidation.Generator/ZValidation.Generator.csproj
```

Expected: Build succeeded, 0 errors.

**Step 6: Commit**

```bash
git add src/ZValidation.Generator/
git commit -m "feat: scaffold ZValidation.Generator (Roslyn incremental generator)"
```

---

### Task 3: Create `ZValidation` core project

**Files:**
- Create: `src/ZValidation/ZValidation.csproj`
- Create: `src/ZValidation/Core/ValidationFailure.cs`
- Create: `src/ZValidation/Core/ValidationResult.cs`
- Create: `src/ZValidation/Core/ValidationContext.cs`
- Create: `src/ZValidation/Core/ValidatorFor.cs`

**Step 1: Scaffold the project**

```bash
dotnet new classlib -n ZValidation -o src/ZValidation
rm src/ZValidation/Class1.cs
mkdir -p src/ZValidation/Core src/ZValidation/Rules
```

**Step 2: Replace the csproj**

Replace `src/ZValidation/ZValidation.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <!-- Analyzers -->
  <ItemGroup>
    <PackageReference Include="ZeroAlloc.Analyzers" PrivateAssets="all">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Meziantou.Analyzer" Version="3.0.19" PrivateAssets="all">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Roslynator.Analyzers" Version="4.15.0" PrivateAssets="all">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="ErrorProne.NET.CoreAnalyzers" Version="0.1.2" PrivateAssets="all">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="ErrorProne.NET.Structs" Version="0.1.2" PrivateAssets="all">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="NetFabric.Hyperlinq.Analyzer" Version="2.3.0" PrivateAssets="all">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <!-- Bundle the source generator into this NuGet package -->
  <ItemGroup>
    <ProjectReference Include="..\ZValidation.Generator\ZValidation.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

**Step 3: Create `ValidationFailure`**

Create `src/ZValidation/Core/ValidationFailure.cs`:

```csharp
namespace ZValidation;

public readonly struct ValidationFailure
{
    public string PropertyName { get; init; }
    public string ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public Severity Severity { get; init; }
}

public enum Severity { Error, Warning, Info }
```

**Step 4: Create `ValidationResult`**

Create `src/ZValidation/Core/ValidationResult.cs`:

```csharp
namespace ZValidation;

public readonly struct ValidationResult
{
    private readonly ValidationFailure[] _failures;

    public ValidationResult(ValidationFailure[] failures)
    {
        _failures = failures;
    }

    public bool IsValid => _failures is null || _failures.Length == 0;
    public ReadOnlySpan<ValidationFailure> Failures => _failures ?? [];
}
```

**Step 5: Create `ValidationContext`**

Create `src/ZValidation/Core/ValidationContext.cs`:

```csharp
namespace ZValidation;

public ref struct ValidationContext<T>
{
    public T Instance { get; }

    public ValidationContext(T instance)
    {
        Instance = instance;
    }
}
```

**Step 6: Create `ValidatorFor<T>`**

Create `src/ZValidation/Core/ValidatorFor.cs`:

```csharp
namespace ZValidation;

public abstract partial class ValidatorFor<T>
{
    public abstract ValidationResult Validate(T instance);
}
```

**Step 7: Add to solution and build**

```bash
dotnet sln add src/ZValidation/ZValidation.csproj
dotnet build src/ZValidation/ZValidation.csproj
```

Expected: Build succeeded, 0 errors.

**Step 8: Commit**

```bash
git add src/ZValidation/
git commit -m "feat: scaffold ZValidation core types"
```

---

### Task 4: Create `ZValidation.AspNetCore` project

**Files:**
- Create: `src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj`
- Create: `src/ZValidation.AspNetCore/Integration/.gitkeep`

**Step 1: Scaffold the project**

```bash
dotnet new classlib -n ZValidation.AspNetCore -o src/ZValidation.AspNetCore
rm src/ZValidation.AspNetCore/Class1.cs
mkdir -p src/ZValidation.AspNetCore/Integration
touch src/ZValidation.AspNetCore/Integration/.gitkeep
```

**Step 2: Replace the csproj**

Replace `src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ZValidation\ZValidation.csproj" />
  </ItemGroup>

</Project>
```

**Step 3: Add to solution and build**

```bash
dotnet sln add src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj
dotnet build src/ZValidation.AspNetCore/ZValidation.AspNetCore.csproj
```

Expected: Build succeeded, 0 errors.

**Step 4: Commit**

```bash
git add src/ZValidation.AspNetCore/
git commit -m "feat: scaffold ZValidation.AspNetCore project"
```

---

### Task 5: Create `ZValidation.Testing` project

**Files:**
- Create: `src/ZValidation.Testing/ZValidation.Testing.csproj`
- Create: `src/ZValidation.Testing/ValidationAssert.cs`

**Step 1: Scaffold the project**

```bash
dotnet new classlib -n ZValidation.Testing -o src/ZValidation.Testing
rm src/ZValidation.Testing/Class1.cs
```

**Step 2: Replace the csproj**

Replace `src/ZValidation.Testing/ZValidation.Testing.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\ZValidation\ZValidation.csproj" />
  </ItemGroup>

</Project>
```

**Step 3: Create `ValidationAssert`**

Create `src/ZValidation.Testing/ValidationAssert.cs`:

```csharp
namespace ZValidation.Testing;

public static class ValidationAssert
{
    public static void HasError(ValidationResult result, string propertyName)
    {
        foreach (var failure in result.Failures)
        {
            if (failure.PropertyName == propertyName)
                return;
        }
        throw new ValidationAssertException(
            $"Expected a validation error for '{propertyName}' but none was found.");
    }

    public static void NoErrors(ValidationResult result)
    {
        if (!result.IsValid)
            throw new ValidationAssertException(
                $"Expected no validation errors but found {result.Failures.Length}.");
    }
}

public sealed class ValidationAssertException(string message) : Exception(message);
```

**Step 4: Add to solution and build**

```bash
dotnet sln add src/ZValidation.Testing/ZValidation.Testing.csproj
dotnet build src/ZValidation.Testing/ZValidation.Testing.csproj
```

Expected: Build succeeded, 0 errors.

**Step 5: Commit**

```bash
git add src/ZValidation.Testing/
git commit -m "feat: scaffold ZValidation.Testing with framework-agnostic assertions"
```

---

### Task 6: Create `ZValidation.Tests` (xUnit)

**Files:**
- Create: `tests/ZValidation.Tests/ZValidation.Tests.csproj`
- Create: `tests/ZValidation.Tests/ValidationResultTests.cs`

**Step 1: Scaffold the project**

```bash
dotnet new xunit -n ZValidation.Tests -o tests/ZValidation.Tests
```

**Step 2: Replace the csproj**

Replace `tests/ZValidation.Tests/ZValidation.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ZValidation\ZValidation.csproj" />
    <ProjectReference Include="..\..\src\ZValidation.Testing\ZValidation.Testing.csproj" />
  </ItemGroup>

</Project>
```

**Step 3: Write the first failing test**

Delete the generated `UnitTest1.cs` and create `tests/ZValidation.Tests/ValidationResultTests.cs`:

```csharp
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests;

public class ValidationResultTests
{
    [Fact]
    public void IsValid_WhenNoFailures_ReturnsTrue()
    {
        var result = new ValidationResult([]);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void IsValid_WhenFailuresPresent_ReturnsFalse()
    {
        var failure = new ValidationFailure { PropertyName = "Name", ErrorMessage = "Required" };
        var result = new ValidationResult([failure]);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidationAssert_HasError_PassesWhenErrorPresent()
    {
        var failure = new ValidationFailure { PropertyName = "Name", ErrorMessage = "Required" };
        var result = new ValidationResult([failure]);
        ValidationAssert.HasError(result, "Name");
    }

    [Fact]
    public void ValidationAssert_HasError_ThrowsWhenErrorAbsent()
    {
        var result = new ValidationResult([]);
        Assert.Throws<ValidationAssertException>(() => ValidationAssert.HasError(result, "Name"));
    }
}
```

**Step 4: Add to solution and run tests**

```bash
dotnet sln add tests/ZValidation.Tests/ZValidation.Tests.csproj
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj
```

Expected: 4 tests pass.

**Step 5: Commit**

```bash
git add tests/ZValidation.Tests/
git commit -m "test: add xUnit test project with ValidationResult smoke tests"
```

---

### Task 7: Create `ZValidation.Tests.NUnit`

**Files:**
- Create: `tests/ZValidation.Tests.NUnit/ZValidation.Tests.NUnit.csproj`
- Create: `tests/ZValidation.Tests.NUnit/ValidationAssertCompatTests.cs`

**Step 1: Scaffold the project**

```bash
dotnet new nunit -n ZValidation.Tests.NUnit -o tests/ZValidation.Tests.NUnit
rm tests/ZValidation.Tests.NUnit/UnitTest1.cs
```

**Step 2: Replace the csproj**

Replace `tests/ZValidation.Tests.NUnit/ZValidation.Tests.NUnit.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NUnit" Version="4.3.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ZValidation\ZValidation.csproj" />
    <ProjectReference Include="..\..\src\ZValidation.Testing\ZValidation.Testing.csproj" />
  </ItemGroup>

</Project>
```

**Step 3: Write compat test**

Create `tests/ZValidation.Tests.NUnit/ValidationAssertCompatTests.cs`:

```csharp
using NUnit.Framework;
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests.NUnit;

[TestFixture]
public class ValidationAssertCompatTests
{
    [Test]
    public void HasError_WorksWithNUnit()
    {
        var failure = new ValidationFailure { PropertyName = "Email", ErrorMessage = "Invalid" };
        var result = new ValidationResult([failure]);
        ValidationAssert.HasError(result, "Email");
    }

    [Test]
    public void NoErrors_WorksWithNUnit()
    {
        var result = new ValidationResult([]);
        ValidationAssert.NoErrors(result);
    }
}
```

**Step 4: Add to solution and run tests**

```bash
dotnet sln add tests/ZValidation.Tests.NUnit/ZValidation.Tests.NUnit.csproj
dotnet test tests/ZValidation.Tests.NUnit/ZValidation.Tests.NUnit.csproj
```

Expected: 2 tests pass.

**Step 5: Commit**

```bash
git add tests/ZValidation.Tests.NUnit/
git commit -m "test: add NUnit compat test project"
```

---

### Task 8: Create `ZValidation.Tests.MSTest`

**Files:**
- Create: `tests/ZValidation.Tests.MSTest/ZValidation.Tests.MSTest.csproj`
- Create: `tests/ZValidation.Tests.MSTest/ValidationAssertCompatTests.cs`

**Step 1: Scaffold the project**

```bash
dotnet new mstest -n ZValidation.Tests.MSTest -o tests/ZValidation.Tests.MSTest
rm tests/ZValidation.Tests.MSTest/UnitTest1.cs
```

**Step 2: Replace the csproj**

Replace `tests/ZValidation.Tests.MSTest/ZValidation.Tests.MSTest.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MSTest.TestAdapter" Version="3.7.3" />
    <PackageReference Include="MSTest.TestFramework" Version="3.7.3" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ZValidation\ZValidation.csproj" />
    <ProjectReference Include="..\..\src\ZValidation.Testing\ZValidation.Testing.csproj" />
  </ItemGroup>

</Project>
```

**Step 3: Write compat test**

Create `tests/ZValidation.Tests.MSTest/ValidationAssertCompatTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests.MSTest;

[TestClass]
public class ValidationAssertCompatTests
{
    [TestMethod]
    public void HasError_WorksWithMSTest()
    {
        var failure = new ValidationFailure { PropertyName = "Email", ErrorMessage = "Invalid" };
        var result = new ValidationResult([failure]);
        ValidationAssert.HasError(result, "Email");
    }

    [TestMethod]
    public void NoErrors_WorksWithMSTest()
    {
        var result = new ValidationResult([]);
        ValidationAssert.NoErrors(result);
    }
}
```

**Step 4: Add to solution and run tests**

```bash
dotnet sln add tests/ZValidation.Tests.MSTest/ZValidation.Tests.MSTest.csproj
dotnet test tests/ZValidation.Tests.MSTest/ZValidation.Tests.MSTest.csproj
```

Expected: 2 tests pass.

**Step 5: Commit**

```bash
git add tests/ZValidation.Tests.MSTest/
git commit -m "test: add MSTest compat test project"
```

---

### Task 9: Full solution build and test

**Step 1: Build entire solution**

```bash
dotnet build ZValidation.sln
```

Expected: Build succeeded across all projects and all TFMs, 0 errors.

**Step 2: Run all tests**

```bash
dotnet test ZValidation.sln
```

Expected: All tests pass across `net8.0`, `net9.0`, `net10.0`.

**Step 3: Final commit**

```bash
git add .
git commit -m "chore: verify full solution build and all tests green"
```

---

## Done

At this point the solution scaffold is complete:
- All 7 projects created and wired up
- Multi-targeting `net8.0;net9.0;net10.0` on all runtime projects
- Generator bundled as analyzer in `ZValidation`
- All analyzers applied to core project
- Smoke tests passing across all three test frameworks
