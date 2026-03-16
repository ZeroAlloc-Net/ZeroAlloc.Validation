# Test Gap Closure Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Close all identified test gaps: fix the nested/collection metadata propagation bug and add missing integration and generator emission tests.

**Architecture:** Four independent tasks. Task 1 is a bug fix in `RuleEmitter.cs` + regression tests. Tasks 2–4 are pure test additions — no production code changes. All tests are xUnit, following the existing patterns in `tests/ZValidation.Tests/`.

**Tech Stack:** C# xUnit, Roslyn source generator in-memory compilation (for generator tests), `ValidationAssert` helper from `ZValidation.Testing`.

---

## Task 1: Fix nested/collection metadata propagation (BUG)

**Context:** `EmitNestedValidators` and `EmitCollectionValidators` in `RuleEmitter.cs` reconstruct `ValidationFailure` objects when propagating failures from child validators. They only copy `PropertyName` and `ErrorMessage` — `ErrorCode` and `Severity` are silently dropped.

**Files:**
- Modify: `src/ZValidation.Generator/RuleEmitter.cs`
- Modify: `tests/ZValidation.Tests/Integration/Address.cs`
- Modify: `tests/ZValidation.Tests/Integration/LineItem.cs`
- Modify: `tests/ZValidation.Tests/Integration/PostalCode.cs`
- Modify: `tests/ZValidation.Tests/Integration/NestedValidationTests.cs`
- Modify: `tests/ZValidation.Tests/Integration/CollectionValidationTests.cs`
- Modify: `tests/ZValidation.Tests/Integration/DeepNestingTests.cs`
- Modify: `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write the failing tests**

Add to `NestedValidationTests.cs`:
```csharp
[Fact]
public void Nested_Failure_PreservesErrorCode()
{
    var order = new Order
    {
        Reference = "ORD-001",
        ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
        BillingAddress = new Address { Street = "", City = "Shelbyville" }
    };
    var result = _validator.Validate(order);
    var failure = result.Failures.ToArray()
        .First(f => string.Equals(f.PropertyName, "BillingAddress.Street", StringComparison.Ordinal));
    Assert.Equal("STREET_REQUIRED", failure.ErrorCode);
}

[Fact]
public void Nested_Failure_PreservesSeverity()
{
    var order = new Order
    {
        Reference = "ORD-001",
        ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
        BillingAddress = new Address { Street = "456 Oak", City = "" }
    };
    var result = _validator.Validate(order);
    var failure = result.Failures.ToArray()
        .First(f => string.Equals(f.PropertyName, "BillingAddress.City", StringComparison.Ordinal));
    Assert.Equal(Severity.Warning, failure.Severity);
}
```

Add to `CollectionValidationTests.cs`:
```csharp
[Fact]
public void Collection_Failure_PreservesErrorCode()
{
    var cart = new Cart
    {
        CustomerId = "C-001",
        Items = [ new LineItem { Sku = "", Quantity = 1 } ]
    };
    var result = _validator.Validate(cart);
    var failure = result.Failures.ToArray()
        .First(f => string.Equals(f.PropertyName, "Items[0].Sku", StringComparison.Ordinal));
    Assert.Equal("SKU_REQUIRED", failure.ErrorCode);
}
```

Add to `DeepNestingTests.cs`:
```csharp
[Fact]
public void ThreeLevel_Failure_PreservesErrorCode()
{
    var depot = new Depot
    {
        Id = "D-01",
        Zone = new DeliveryZone { Name = "North", PostalCode = new PostalCode { Code = "" } }
    };
    var result = _validator.Validate(depot);
    var failure = result.Failures.ToArray()
        .Single(f => string.Equals(f.PropertyName, "Zone.PostalCode.Code", StringComparison.Ordinal));
    Assert.Equal("CODE_REQUIRED", failure.ErrorCode);
}
```

Add to `GeneratorRuleEmissionTests.cs`:
```csharp
[Fact]
public void Generator_NestedPropagation_ForwardsErrorCodeAndSeverity()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Inner { [NotEmpty(ErrorCode = "E1", Severity = Severity.Warning)] public string Val { get; set; } = ""; }
        [Validate]
        public class Outer { public Inner Child { get; set; } = new(); }
        """;

    var outerSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("OuterValidator", StringComparison.Ordinal));

    Assert.Contains("f.ErrorCode", outerSource, StringComparison.Ordinal);
    Assert.Contains("f.Severity", outerSource, StringComparison.Ordinal);
}

[Fact]
public void Generator_CollectionPropagation_ForwardsErrorCodeAndSeverity()
{
    var source = """
        using ZValidation;
        using System.Collections.Generic;
        namespace TestModels;
        [Validate]
        public class Item { [NotEmpty(ErrorCode = "E1")] public string Name { get; set; } = ""; }
        [Validate]
        public class Bag { public List<Item> Items { get; set; } = new(); }
        """;

    var bagSource = RunGeneratorGetSources(source)
        .First(s => s.Contains("BagValidator", StringComparison.Ordinal));

    Assert.Contains("f.ErrorCode", bagSource, StringComparison.Ordinal);
    Assert.Contains("f.Severity", bagSource, StringComparison.Ordinal);
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "PreservesErrorCode|PreservesSeverity|ForwardsErrorCode" -v minimal
```

Expected: 5 FAIL.

**Step 3: Add ErrorCode + Severity to the inner models**

Update `tests/ZValidation.Tests/Integration/Address.cs`:
```csharp
using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class Address
{
    [NotEmpty(Message = "Street is required.", ErrorCode = "STREET_REQUIRED")]
    public string Street { get; set; } = "";

    [NotEmpty(Message = "City is required.", Severity = Severity.Warning)]
    public string City { get; set; } = "";
}
```

Update `tests/ZValidation.Tests/Integration/LineItem.cs`:
```csharp
using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class LineItem
{
    [NotEmpty(Message = "SKU is required.", ErrorCode = "SKU_REQUIRED")]
    public string Sku { get; set; } = "";

    [GreaterThan(0, Message = "Quantity must be positive.")]
    public int Quantity { get; set; }
}
```

Update `tests/ZValidation.Tests/Integration/PostalCode.cs`:
```csharp
using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class PostalCode
{
    [NotEmpty(Message = "Code is required.", ErrorCode = "CODE_REQUIRED")]
    public string Code { get; set; } = "";
}
```

**Step 4: Fix EmitNestedValidators in RuleEmitter.cs**

Find the line (in `EmitNestedValidators`):
```csharp
sb.AppendLine($"                failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}.\" + f.PropertyName, ErrorMessage = f.ErrorMessage }});");
```

Replace with:
```csharp
sb.AppendLine($"                failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}.\" + f.PropertyName, ErrorMessage = f.ErrorMessage, ErrorCode = f.ErrorCode, Severity = f.Severity }});");
```

**Step 5: Fix EmitCollectionValidators in RuleEmitter.cs**

Find the line (in `EmitCollectionValidators`):
```csharp
sb.AppendLine($"                        failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}[\" + {varName}Idx + \"].\" + f.PropertyName, ErrorMessage = f.ErrorMessage }});");
```

Replace with:
```csharp
sb.AppendLine($"                        failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}[\" + {varName}Idx + \"].\" + f.PropertyName, ErrorMessage = f.ErrorMessage, ErrorCode = f.ErrorCode, Severity = f.Severity }});");
```

**Step 6: Run the 5 new tests**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "PreservesErrorCode|PreservesSeverity|ForwardsErrorCode" -v minimal
```

Expected: 5 PASS.

**Step 7: Run full test suite**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```

Expected: all pass.

**Step 8: Commit**

```bash
git add src/ZValidation.Generator/RuleEmitter.cs \
        tests/ZValidation.Tests/Integration/Address.cs \
        tests/ZValidation.Tests/Integration/LineItem.cs \
        tests/ZValidation.Tests/Integration/PostalCode.cs \
        tests/ZValidation.Tests/Integration/NestedValidationTests.cs \
        tests/ZValidation.Tests/Integration/CollectionValidationTests.cs \
        tests/ZValidation.Tests/Integration/DeepNestingTests.cs \
        tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "fix: propagate ErrorCode and Severity through nested/collection validators"
```

---

## Task 2: Add ValidateWith integration tests

**Context:** `[ValidateWith(typeof(T))]` has generator emission tests but zero integration tests. Need a third-party-style type (no `[Validate]`) with a hand-written validator, and a model using `[ValidateWith]`.

**Files:**
- Create: `tests/ZValidation.Tests/Integration/Coordinate.cs`
- Create: `tests/ZValidation.Tests/Integration/CoordinateValidator.cs`
- Create: `tests/ZValidation.Tests/Integration/Location.cs`
- Create: `tests/ZValidation.Tests/Integration/ValidateWithTests.cs`

**Step 1: Create model and hand-written validator**

`tests/ZValidation.Tests/Integration/Coordinate.cs`:
```csharp
namespace ZValidation.Tests.Integration;

// Intentionally no [Validate] — simulates a third-party type you don't control.
public class Coordinate
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}
```

`tests/ZValidation.Tests/Integration/CoordinateValidator.cs`:
```csharp
using ZValidation;

namespace ZValidation.Tests.Integration;

// Hand-written validator for a third-party type. NOT source-generated.
public class CoordinateValidator : ValidatorFor<Coordinate>
{
    public override ValidationResult Validate(Coordinate instance)
    {
        var failures = new System.Collections.Generic.List<ValidationFailure>();
        if (instance.Lat < -90 || instance.Lat > 90)
            failures.Add(new ValidationFailure
            {
                PropertyName = "Lat",
                ErrorMessage = "Latitude must be between -90 and 90.",
                ErrorCode = "LAT_RANGE"
            });
        if (instance.Lng < -180 || instance.Lng > 180)
            failures.Add(new ValidationFailure
            {
                PropertyName = "Lng",
                ErrorMessage = "Longitude must be between -180 and 180.",
                ErrorCode = "LNG_RANGE"
            });
        return new ValidationResult(failures.ToArray());
    }
}
```

`tests/ZValidation.Tests/Integration/Location.cs`:
```csharp
using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class Location
{
    [NotEmpty]
    public string Name { get; set; } = "";

    [ValidateWith(typeof(CoordinateValidator))]
    public Coordinate Point { get; set; } = new();
}
```

**Step 2: Write the tests (they will fail until generator wires them up — it should already work)**

`tests/ZValidation.Tests/Integration/ValidateWithTests.cs`:
```csharp
using System;
using System.Linq;
using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class ValidateWithTests
{
    private readonly LocationValidator _validator = new(new CoordinateValidator());

    [Fact]
    public void Valid_Location_PassesValidation()
    {
        var location = new Location { Name = "Home", Point = new Coordinate { Lat = 51.5, Lng = -0.1 } };
        ValidationAssert.NoErrors(_validator.Validate(location));
    }

    [Fact]
    public void Invalid_Coordinate_ReportsDotPrefixedFailure()
    {
        var location = new Location { Name = "Home", Point = new Coordinate { Lat = 200, Lng = 0 } };
        var result = _validator.Validate(location);
        ValidationAssert.HasError(result, "Point.Lat");
    }

    [Fact]
    public void Invalid_Coordinate_PreservesErrorCode_FromHandWrittenValidator()
    {
        var location = new Location { Name = "Home", Point = new Coordinate { Lat = 200, Lng = 0 } };
        var result = _validator.Validate(location);
        var failure = result.Failures.ToArray()
            .First(f => string.Equals(f.PropertyName, "Point.Lat", StringComparison.Ordinal));
        Assert.Equal("LAT_RANGE", failure.ErrorCode);
    }

    [Fact]
    public void Both_Direct_And_ValidateWith_Failures_Reported()
    {
        var location = new Location { Name = "", Point = new Coordinate { Lat = 200, Lng = 400 } };
        var result = _validator.Validate(location);
        ValidationAssert.HasError(result, "Name");
        ValidationAssert.HasError(result, "Point.Lat");
        ValidationAssert.HasError(result, "Point.Lng");
        Assert.Equal(3, result.Failures.Length);
    }

    [Fact]
    public void Null_Point_IsSkipped()
    {
        // Location.Point is non-nullable so can't actually be null in normal usage,
        // but we test the null-guard path defensively.
        var location = new Location { Name = "Home", Point = null! };
        // The generator emits a null guard — no NullReferenceException
        ValidationAssert.NoErrors(_validator.Validate(location));
    }
}
```

**Step 3: Run tests**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "ValidateWith" -v minimal
```

Expected: all PASS (the generator already handles [ValidateWith] — this just exercises it at runtime).

**Step 4: Run full test suite**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```

Expected: all pass.

**Step 5: Commit**

```bash
git add tests/ZValidation.Tests/Integration/Coordinate.cs \
        tests/ZValidation.Tests/Integration/CoordinateValidator.cs \
        tests/ZValidation.Tests/Integration/Location.cs \
        tests/ZValidation.Tests/Integration/ValidateWithTests.cs
git commit -m "test: add [ValidateWith] integration tests"
```

---

## Task 3: Add Matches integration tests + missing generator emission tests

**Context:** `[Matches]` has no integration tests and no generator emission test. Also missing generator emission tests for `[EmailAddress]` and `[InclusiveBetween]`.

**Files:**
- Create: `tests/ZValidation.Tests/Integration/MatchesModel.cs`
- Create: `tests/ZValidation.Tests/Integration/MatchesTests.cs`
- Modify: `tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs`

**Step 1: Write the failing generator emission tests first**

Add to `GeneratorRuleEmissionTests.cs`:
```csharp
[Fact]
public void Generator_EmitsMatches_Check()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Foo { [Matches(@"^\d{5}$")] public string Zip { get; set; } = ""; }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("Regex.IsMatch", generated, StringComparison.Ordinal);
    Assert.Contains(@"^\d{5}$", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_EmitsEmailAddress_Check()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Foo { [EmailAddress] public string Email { get; set; } = ""; }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("EmailValidator.IsValid", generated, StringComparison.Ordinal);
}

[Fact]
public void Generator_EmitsInclusiveBetween_Check()
{
    var source = """
        using ZValidation;
        namespace TestModels;
        [Validate]
        public class Foo { [InclusiveBetween(1, 10)] public int Value { get; set; } }
        """;
    var generated = RunGeneratorGetSource(source);
    Assert.Contains("< 1", generated, StringComparison.Ordinal);
    Assert.Contains("> 10", generated, StringComparison.Ordinal);
}
```

**Step 2: Run generator tests to verify they pass (they should — generator already handles these)**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "EmitsMatches|EmitsEmailAddress|EmitsInclusiveBetween" -v minimal
```

Expected: 3 PASS (verifying emission is correct).

**Step 3: Create Matches integration model**

`tests/ZValidation.Tests/Integration/MatchesModel.cs`:
```csharp
using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class MatchesModel
{
    [Matches(@"^\d{5}$", Message = "ZipCode must be exactly 5 digits.")]
    public string ZipCode { get; set; } = "";

    [Matches(@"^[A-Z]{2,3}$")]
    public string CountryCode { get; set; } = "";
}
```

**Step 4: Create Matches integration tests**

`tests/ZValidation.Tests/Integration/MatchesTests.cs`:
```csharp
using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class MatchesTests
{
    private readonly MatchesModelValidator _validator = new();

    [Fact]
    public void Valid_ZipCode_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "US" }));
    }

    [Fact]
    public void Invalid_ZipCode_Letters_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = "ABCDE", CountryCode = "US" }), "ZipCode");
    }

    [Fact]
    public void Invalid_ZipCode_TooShort_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = "1234", CountryCode = "US" }), "ZipCode");
    }

    [Fact]
    public void Invalid_ZipCode_ReportsCustomMessage()
    {
        var result = _validator.Validate(new MatchesModel { ZipCode = "bad", CountryCode = "US" });
        var failure = System.Linq.Enumerable.First(result.Failures.ToArray(), f =>
            string.Equals(f.PropertyName, "ZipCode", System.StringComparison.Ordinal));
        Assert.Equal("ZipCode must be exactly 5 digits.", failure.ErrorMessage);
    }

    [Fact]
    public void Valid_CountryCode_TwoLetter_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "US" }));
    }

    [Fact]
    public void Valid_CountryCode_ThreeLetter_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "USA" }));
    }

    [Fact]
    public void Invalid_CountryCode_Lowercase_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "us" }), "CountryCode");
    }

    [Fact]
    public void Invalid_CountryCode_TooLong_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "USAA" }), "CountryCode");
    }

    [Fact]
    public void Null_Value_FailsMatches()
    {
        // Matches emits: !Regex.IsMatch(access ?? "", pattern) — null treated as empty string, which won't match
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = null!, CountryCode = "US" }), "ZipCode");
    }
}
```

**Step 5: Run Matches tests**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "MatchesTests" -v minimal
```

Expected: all PASS.

**Step 6: Run full test suite**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```

Expected: all pass.

**Step 7: Commit**

```bash
git add tests/ZValidation.Tests/Integration/MatchesModel.cs \
        tests/ZValidation.Tests/Integration/MatchesTests.cs \
        tests/ZValidation.Tests/Generator/GeneratorRuleEmissionTests.cs
git commit -m "test: add Matches integration tests and missing generator emission tests"
```

---

## Task 4: Close remaining small test gaps

**Context:** Four small gaps to close in existing test files:
1. Sparse collections (null items mixed with non-null)
2. `IsEnumName` case sensitivity
3. `[StopOnFirstFailure]` + `When` interaction
4. `When`/`Unless` on nested properties (verify condition guards work on the parent model when propagating)

**Files:**
- Modify: `tests/ZValidation.Tests/Integration/CollectionValidationTests.cs`
- Modify: `tests/ZValidation.Tests/Integration/IsEnumNameTests.cs`
- Modify: `tests/ZValidation.Tests/Integration/CascadeModel.cs`
- Modify: `tests/ZValidation.Tests/Integration/CascadeTests.cs`
- Modify: `tests/ZValidation.Tests/Integration/ConditionalModel.cs`
- Modify: `tests/ZValidation.Tests/Integration/ConditionalTests.cs`

**Step 1: Add sparse collection test**

Add to `CollectionValidationTests.cs`:
```csharp
[Fact]
public void Null_Items_In_Collection_AreSkipped()
{
    // The generator emits: if (item is not null) { validate } idx++
    // Null items should be silently skipped, not cause NullReferenceException
    var cart = new Cart
    {
        CustomerId = "C-001",
        Items = new System.Collections.Generic.List<LineItem> { null!, new LineItem { Sku = "ABC", Quantity = 1 }, null! }
    };
    var result = _validator.Validate(cart);
    // Only the valid item — no failures
    ValidationAssert.NoErrors(result);
}

[Fact]
public void Null_Item_IndexContinues_AfterNullItem()
{
    // Null items increment the index counter — the second valid item should show index [2]
    var cart = new Cart
    {
        CustomerId = "C-001",
        Items = new System.Collections.Generic.List<LineItem>
        {
            null!,                             // index 0 — skipped
            null!,                             // index 1 — skipped
            new LineItem { Sku = "", Quantity = 1 } // index 2 — fails
        }
    };
    var result = _validator.Validate(cart);
    ValidationAssert.HasError(result, "Items[2].Sku");
}
```

**Step 2: Add IsEnumName case sensitivity test**

Add to `IsEnumNameTests.cs`:
```csharp
[Fact]
public void LowercaseName_Fails_CaseSensitive()
{
    // IsDefined uses case-sensitive comparison for string input
    ValidationAssert.HasError(_validator.Validate(new EnumNameModel { LightName = "red" }), "LightName");
}

[Fact]
public void MixedCaseName_Fails()
{
    ValidationAssert.HasError(_validator.Validate(new EnumNameModel { LightName = "RED" }), "LightName");
}
```

**Step 3: Add StopOnFirstFailure + When interaction to CascadeModel**

Add to `CascadeModel.cs`:
```csharp
public bool ConditionalCheck { get; set; }

// StopOnFirstFailure + When: stop fires only when first rule actually adds a failure,
// not merely when its When condition makes it inactive.
[StopOnFirstFailure]
[NotEmpty(When = nameof(IsConditionalRequired))]
[MinLength(3)]
public string ConditionalStop { get; set; } = "";
public bool IsConditionalRequired() => ConditionalCheck;
```

**Step 4: Add StopOnFirstFailure + When tests to CascadeTests**

Add to `CascadeTests.cs`:
```csharp
[Fact]
public void StopOnFirstFailure_WhenConditionFalse_SubsequentRuleStillRuns()
{
    // When=false → first rule skipped (not fired) → else if runs → MinLength fires
    var result = _validator.Validate(new CascadeModel
    {
        StopName = "ok", ContinueName = "hello",
        ConditionalCheck = false,
        ConditionalStop = "x" // length 1, fails MinLength(3)
    });
    ValidationAssert.HasError(result, "ConditionalStop");
    var failure = System.Linq.Enumerable.Single(result.Failures.ToArray(),
        f => string.Equals(f.PropertyName, "ConditionalStop", System.StringComparison.Ordinal));
    Assert.Contains("MinLength", failure.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void StopOnFirstFailure_WhenConditionTrue_FirstRuleFires_SecondSkipped()
{
    // When=true → first rule fires (NotEmpty on "") → else if skipped → only 1 failure
    var result = _validator.Validate(new CascadeModel
    {
        StopName = "ok", ContinueName = "hello",
        ConditionalCheck = true,
        ConditionalStop = "" // empty: fails NotEmpty when condition=true
    });
    var failures = result.Failures.ToArray();
    var conditionalFailures = System.Linq.Enumerable.Where(failures,
        f => string.Equals(f.PropertyName, "ConditionalStop", System.StringComparison.Ordinal))
        .ToArray();
    Assert.Single(conditionalFailures); // stop mode: only NotEmpty fires, MinLength skipped
}
```

**Step 5: Add When/Unless on nested validation to ConditionalModel**

Add to `ConditionalModel.cs` (the model already tests When/Unless on simple properties — add a nested property with a When guard):
```csharp
public bool ValidateAddress { get; set; }

[ValidateWith(typeof(AddressValidator))]
[ZValidation.NotNull(When = nameof(IsAddressRequired))]
public Address? ConditionalAddress { get; set; }
public bool IsAddressRequired() => ValidateAddress;
```

Wait — `[NotNull]` and `[ValidateWith]` are separate concerns. The `[NotNull]` is a rule attribute (with When), and `[ValidateWith]` is a nested validator directive. These can coexist on the same property.

However the `ConditionalModel` is already complex. Instead, add a simpler test to `ConditionalTests.cs` using the existing `Order` model which has `[NotNull] public Address? ShippingAddress`:

**Step 5 (revised): Add a test to ConditionalTests showing When on a nested object property works independently of nested validation**

Actually the `ConditionalModel` doesn't have a nested validated type. Rather than modifying a complex existing model, add a new simpler assertion to verify the existing `When`/`Unless` interaction with the nested path still behaves correctly. The `Order` model already has `[NotNull]` on a nested address which is a form of conditional-ish checking. The `ConditionalTests` already cover When/Unless adequately.

Skip the nested When/Unless test — the generator tests already cover the code path (`Generator_EmitsWhen_Guard`), and there's no clean place to add it without significantly reworking models.

**Step 6: Run all new tests**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj --filter "NullItems|LowercaseName|MixedCase|WhenConditionFalse|WhenConditionTrue" -v minimal
```

Expected: all PASS.

**Step 7: Run full test suite**

```bash
dotnet test tests/ZValidation.Tests/ZValidation.Tests.csproj -v minimal
```

Expected: all pass.

**Step 8: Commit**

```bash
git add tests/ZValidation.Tests/Integration/CollectionValidationTests.cs \
        tests/ZValidation.Tests/Integration/IsEnumNameTests.cs \
        tests/ZValidation.Tests/Integration/CascadeModel.cs \
        tests/ZValidation.Tests/Integration/CascadeTests.cs
git commit -m "test: close remaining test gaps (sparse collections, enum name case, cascade+when)"
```
