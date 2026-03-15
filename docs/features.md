# ZValidation Feature Specification

> A **code-generated, zero-allocation** validation library for .NET.

**Legend:** ✅ Done &nbsp;|&nbsp; ⬜ Pending

---

## Design Goals

- **Zero allocations** on the hot path — no `List<ValidationFailure>` heap allocations per validation call
- **Source-generated** validator implementations — no reflection at runtime
- **Strongly typed** fluent API — full IntelliSense, no magic strings
- **AOT/NativeAOT friendly** — no dynamic code generation or reflection
- Validation results returned as `ref struct` or pooled/stack-allocated result types

---

## 1. Core Validator API ✅

### 1.1 Validator Definition ✅
Define validators by implementing a source-generated interface or annotating a partial class:

```csharp
[Validator]
public partial class CustomerValidator : ValidatorFor<Customer>
{
    public CustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Age).GreaterThan(0).LessThan(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
```

### 1.2 Validation Execution ⬜

```csharp
var result = validator.Validate(customer);          // sync
var result = await validator.ValidateAsync(customer); // async
validator.ValidateAndThrow(customer);               // throws on failure
```

### 1.3 Zero-Allocation Result ✅

- `ValidationResult` is a `ref struct` or uses a pooled buffer
- Failures stored in a `Span<ValidationFailure>` or `stackalloc`-backed list
- No `IEnumerable` boxing; failures exposed as `ReadOnlySpan<ValidationFailure>`

---

## 2. Built-in Validators ✅

### Null / Empty
| Rule | Description |
|------|-------------|
| `NotNull()` | Property must not be `null` |
| `Null()` | Property must be `null` |
| `NotEmpty()` | Not null, not empty string/collection, not default value |
| `Empty()` | Must be null, empty, or default |

### Equality
| Rule | Description |
|------|-------------|
| `Equal(value)` | Must equal a constant or another property |
| `NotEqual(value)` | Must not equal a constant or another property |

### String Length
| Rule | Description |
|------|-------------|
| `Length(min, max)` | String length within range |
| `MinimumLength(n)` | String length ≥ n |
| `MaximumLength(n)` | String length ≤ n |

### Comparison (numeric / comparable)
| Rule | Description |
|------|-------------|
| `LessThan(n)` | Value < n (or another property) |
| `LessThanOrEqualTo(n)` | Value ≤ n |
| `GreaterThan(n)` | Value > n |
| `GreaterThanOrEqualTo(n)` | Value ≥ n |
| `ExclusiveBetween(min, max)` | min < value < max |
| `InclusiveBetween(min, max)` | min ≤ value ≤ max |

### Format / Pattern
| Rule | Description |
|------|-------------|
| `Matches(regex)` | Matches a regular expression |
| `EmailAddress()` | Valid email address format |
| `CreditCard()` | Luhn-valid credit card number |

### Enum
| Rule | Description |
|------|-------------|
| `IsInEnum()` | Numeric value is defined in the enum |
| `IsEnumName()` | String is a valid enum member name |

### Decimal Precision
| Rule | Description |
|------|-------------|
| `PrecisionScale(precision, scale)` | Decimal fits within precision/scale |

---

## 3. Rule Chaining ⬜

Rules on the same property chain with `.`:

```csharp
RuleFor(x => x.Name)
    .NotEmpty()
    .MinimumLength(2)
    .MaximumLength(100)
    .WithMessage("Name must be between 2 and 100 characters.");
```

---

## 4. Custom Validators ⬜

### 4.1 Predicate (`Must`) ✅

Attribute-based (no fluent API): `[Must(nameof(MethodName))]` where the model has `bool MethodName(PropertyType value)`.

```csharp
[Must(nameof(NameStartsWithA))]
public string Name { get; set; }
private bool NameStartsWithA(string value) => value.StartsWith("A", StringComparison.Ordinal);
```

### 4.2 Custom Method (multiple failures)

```csharp
RuleFor(x => x.Address).Custom((address, context) =>
{
    if (!IsValid(address))
        context.AddFailure("Address", "Invalid address format.");
});
```

### 4.3 `PropertyValidator<T, TProperty>` (reusable class)

Implement a strongly-typed validator class for complex logic that can be reused across multiple validators. Source generator emits the dispatch code.

---

## 5. Error Message Configuration ⬜

### 5.1 `WithMessage` ✅

Every validation attribute accepts a `Message` named parameter:

```csharp
[GreaterThan(0, Message = "Age must be positive.")]
public int Age { get; set; }
```

### 5.2 Placeholders (zero-alloc interpolation via source gen) ✅

| Placeholder | Description | Status |
|-------------|-------------|--------|
| `{PropertyName}` | Display name of the property | ✅ |
| `{ComparisonValue}` | Value used in comparison validators | ✅ |
| `{MinLength}` / `{MaxLength}` | Used in length validators | ✅ |
| `{From}` / `{To}` | Used in between validators | ✅ |
| `{PropertyValue}` | Actual value that failed | ⬜ (requires runtime allocation) |

All supported placeholders are resolved at code-gen time into string literals — no runtime allocation.

### 5.3 `WithName` / `OverridePropertyName`

```csharp
RuleFor(x => x.Forename).NotEmpty().WithName("First Name");
```

### 5.4 `WithErrorCode`

```csharp
RuleFor(x => x.Email).EmailAddress().WithErrorCode("ERR_EMAIL_INVALID");
```

### 5.5 `WithSeverity`

```csharp
RuleFor(x => x.MiddleName).NotEmpty().WithSeverity(Severity.Warning);
```

Severity levels: `Error` (default), `Warning`, `Info`.

### 5.6 `WithState`

Attach custom state to a failure without extra allocations (stored as a typed value, not `object`):

```csharp
RuleFor(x => x.Age).GreaterThan(0).WithState(x => new { x.Id });
```

---

## 6. Conditional Validation ⬜

### 6.1 `When` / `Unless` ✅

Attribute-based (no fluent API): `When` and `Unless` are named params on every validation attribute. The referenced method is an instance method on the model with signature `bool MethodName()`.

```csharp
[NotNull(When = nameof(ShippingRequired))]
public Address? ShippingAddress { get; set; }
private bool ShippingRequired() => RequiresShipping;

[MinLength(5, Unless = nameof(ShortNameOk))]
public string Name { get; set; }
private bool ShortNameOk() => AllowShortName;
```

### 6.2 Top-level `When` Block

```csharp
When(x => x.IsPreferredCustomer, () =>
{
    RuleFor(x => x.Discount).GreaterThan(0);
    RuleFor(x => x.CreditCardNumber).NotNull();
}).Otherwise(() =>
{
    RuleFor(x => x.Discount).Empty();
});
```

### 6.3 `WhenAsync` / `UnlessAsync`

Async predicates for conditions that require I/O.

### 6.4 `ApplyConditionTo`

Controls whether a condition applies to `CurrentValidator` only or `AllValidators` in the chain (default: `AllValidators`).

---

## 7. Cascade Modes ⬜

### 7.1 Rule-Level Cascade (`CascadeMode`)

- `Continue` (default) — run all validators in the chain
- `Stop` — stop at first failure within the chain

```csharp
RuleFor(x => x.Name).Cascade(CascadeMode.Stop).NotNull().NotEmpty();
```

### 7.2 Validator-Level Cascade (`ClassLevelCascadeMode`)

- `Continue` (default) — run all rules
- `Stop` — stop at first failing rule ("fail fast")

```csharp
ClassLevelCascadeMode = CascadeMode.Stop;
```

### 7.3 Global Defaults

Configurable at startup via `ValidatorOptions`.

---

## 8. Complex Property Validation ⬜

Validate nested objects with their own validator:

```csharp
RuleFor(x => x.Address).SetValidator(new AddressValidator());
```

---

## 9. Collection Validation ⬜

### 9.1 `RuleForEach`

Apply rules to every element in a collection:

```csharp
RuleForEach(x => x.Orders).SetValidator(new OrderValidator());
RuleForEach(x => x.Tags).NotEmpty().MaximumLength(50);
```

### 9.2 Index Customization

Override how collection indices appear in property names in failure messages.

---

## 10. RuleSets ⬜

Group rules by name and execute selectively:

```csharp
RuleSet("Create", () =>
{
    RuleFor(x => x.Password).NotEmpty();
});

RuleSet("Update", () =>
{
    RuleFor(x => x.Id).NotEmpty();
});
```

Execute specific sets:
```csharp
validator.Validate(model, options => options.IncludeRuleSets("Create"));
validator.Validate(model, options => options.IncludeAllRuleSets());
```

---

## 11. Dependent Rules ⬜

Run follow-up rules only when a preceding rule passes:

```csharp
RuleFor(x => x.Email)
    .NotEmpty()
    .DependentRules(() =>
    {
        RuleFor(x => x.Email).EmailAddress();
    });
```

---

## 12. Inheritance / Polymorphic Validation ⬜

Validate derived types with type-specific rules:

```csharp
RuleFor(x => x.Shape).SetInheritanceValidator(v =>
{
    v.Add<Circle>(new CircleValidator());
    v.Add<Rectangle>(new RectangleValidator());
});
```

---

## 13. Async Validation ⬜

```csharp
RuleFor(x => x.Username)
    .MustAsync(async (username, ct) => await _repo.IsUniqueAsync(username, ct));
```

- Use `ValidateAsync` to execute async validators
- `CustomAsync` for async custom validators
- ASP.NET automatic model binding pipeline: **sync only** (framework limitation)

---

## 14. Rule Inclusion / Reuse ⬜

Include all rules from another validator:

```csharp
Include(new BaseCustomerValidator());
```

---

## 15. Pre-Validation Hook ⬜

Override `PreValidate` to short-circuit validation before rules run (e.g., null-model guard):

```csharp
protected override bool PreValidate(ValidationContext<Customer> context, ValidationResult result)
{
    if (context.InstanceToValidate is null)
    {
        result.Errors.Add(new ValidationFailure("", "Model must not be null."));
        return false;
    }
    return true;
}
```

---

## 16. Root Context Data ⬜

Pass arbitrary data into the validation pipeline without changing the model:

```csharp
var context = new ValidationContext<Customer>(customer);
context.RootContextData["CurrentUserId"] = userId;
validator.Validate(context);
```

In zero-alloc design, this is passed as a typed parameter to avoid `object` boxing.

---

## 17. Dependency Injection ⬜

Validators integrate with **[ZInject](https://github.com/MarcelRoozekrans/ZInject)** — a compile-time DI source generator that eliminates runtime reflection and scanning.

Decorate validators with a lifetime attribute; ZInject generates the `IServiceCollection` registration automatically:

```csharp
[Scoped]
public partial class CustomerValidator : ValidatorFor<Customer> { ... }

[Transient]
public partial class OrderValidator : ValidatorFor<Order> { ... }

[Singleton]
public partial class CountryValidator : ValidatorFor<Country> { ... }
```

Supported lifetimes: `[Transient]`, `[Scoped]`, `[Singleton]`.

ZInject registers each validator against its implemented interfaces and concrete type using `TryAdd` semantics. Resolution uses a generated type-switch — no dictionary lookups or reflection at runtime.

---

## 18. Localization ⬜

- Override default error messages globally
- Plug in custom resource providers
- Support for multiple languages via resource files
- Source generator can emit localized message resolvers at compile time

---

## 19. Test Extensions ✅

```csharp
// Assert a rule exists for a property
validator.ShouldHaveValidationErrorFor(x => x.Name, "");
validator.ShouldNotHaveValidationErrorFor(x => x.Name, "John");
```

---

## 20. Zero-Allocation Specifics ⬜

Features enabled by source generation:

| Feature | Description |
|---------|-------------|
| `Span<ValidationFailure>` results | Failures stored on stack or in pooled buffer — no heap list | ⬜ |
| Source-generated dispatch | All rule calls inlined at compile time, no virtual dispatch or reflection | ⬜ |
| Struct-based `ValidationFailure` | Failure type is a `readonly struct` to avoid heap allocation | ✅ |
| Compile-time message formatting | Placeholder substitution compiled into efficient `string.Create` or interpolated string handlers | ⬜ |
| `ref struct ValidationContext` | Context passed by reference, never heap-allocated | ✅ |
| No boxing for `WithState<T>` | State is stored as a typed generic field, not `object` | ⬜ |
| AOT / NativeAOT safe | No `Activator.CreateInstance`, no reflection-based rule discovery | ⬜ |
| Pooled result buffers | Optional `ArrayPool<ValidationFailure>` integration for larger result sets | ⬜ |

---

## 21. ASP.NET Core Integration ⬜

- Auto-validate models in controllers via `IActionFilter`
- Return `ValidationProblemDetails` on failure
- Integrates with `IValidateOptions<T>` for options validation
- Source-generated registration extension methods

---

## 22. Analyzers ✅

ZValidation enforces correctness and zero-allocation constraints at compile time via a curated set of Roslyn analyzers. All are analyzer-only dependencies (no runtime impact).

| Package | Purpose |
|---------|---------|
| `ZeroAlloc.Analyzers` | Detects allocation patterns (boxing, closures, LINQ, etc.) that violate zero-alloc constraints |
| `Meziantou.Analyzer` | General correctness, performance, and API usage rules |
| `Roslynator.Analyzers` | Code quality, style, and refactoring diagnostics |
| `ErrorProne.NET.CoreAnalyzers` | Catches common correctness mistakes (e.g., unused results, exception handling) |
| `ErrorProne.NET.Structs` | Enforces safe `struct` usage — detects defensive copies, missing `readonly`, etc. |
| `NetFabric.Hyperlinq.Analyzer` | Identifies LINQ usage that should be replaced with zero-allocation enumeration |

```xml
<ItemGroup>
  <PackageReference Include="ZeroAlloc.Analyzers">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
  <PackageReference Include="Meziantou.Analyzer" Version="3.0.19">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
  <PackageReference Include="Roslynator.Analyzers" Version="4.15.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
  <PackageReference Include="ErrorProne.NET.CoreAnalyzers" Version="0.1.2">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
  <PackageReference Include="ErrorProne.NET.Structs" Version="0.1.2">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
  <PackageReference Include="NetFabric.Hyperlinq.Analyzer" Version="2.3.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

---

## Out of Scope (for now)

- Blazor integration (future milestone)
