# ZValidation Feature Specification

> A **code-generated, zero-allocation** validation library for .NET.

**Legend:** ✅ Done &nbsp;|&nbsp; ⬜ Pending

---

## Design Goals

- **Zero allocations** on the hot path — no `List<ValidationFailure>` heap allocations per validation call
- **Source-generated** validator implementations — no reflection at runtime
- **Attribute-based** decorator API — rules declared directly on model properties, full IntelliSense, no magic strings
- **AOT/NativeAOT friendly** — no dynamic code generation or reflection
- Validation results returned as value types backed by a stack-allocated or preallocated buffer

---

## 1. Core Validator API ✅

### 1.1 Validator Definition ✅

Annotate a model class with `[Validate]`; the source generator produces a `{ClassName}Validator` extending `ValidatorFor<T>`:

```csharp
[Validate]
public class Customer
{
    [NotEmpty]
    [MaxLength(100)]
    public string Name { get; set; } = "";

    [GreaterThan(0)]
    [LessThan(120)]
    public int Age { get; set; }

    [NotEmpty]
    [EmailAddress]
    public string Email { get; set; } = "";
}

// Generated: CustomerValidator : ValidatorFor<Customer>
var validator = new CustomerValidator();
```

### 1.2 Validation Execution ✅

```csharp
var result = validator.Validate(customer);  // sync
bool ok = result.IsValid;
ReadOnlySpan<ValidationFailure> failures = result.Failures;
```

Async execution and `ValidateAndThrow` are out of scope (zero-alloc constraint; see §13).

### 1.3 Zero-Allocation Result ✅

- `ValidationResult` is a `readonly struct` wrapping a `ValidationFailure[]`
- `Failures` exposed as `ReadOnlySpan<ValidationFailure>` — no `IEnumerable` boxing
- `ValidationFailure` is a `readonly struct` with init properties: `PropertyName`, `ErrorMessage`, `ErrorCode`, `Severity`
- Flat-path (no nested/collection properties): preallocated array sized to total rule count, trimmed to actual failure count — zero heap allocation on the hot path
- Mixed-path (nested or collection): accumulates into a `List<ValidationFailure>` then converts; allocation occurs only when nested validation is present

---

## 2. Built-in Validators ✅

All attributes inherit `ValidationAttribute` and support `Message`, `When`, `Unless`, `ErrorCode`, and `Severity` named parameters.

### Null / Empty
| Attribute | Description |
|-----------|-------------|
| `[NotNull]` | Property must not be `null` |
| `[Null]` | Property must be `null` |
| `[NotEmpty]` | Not null and not empty string (or not default for value types) |
| `[Empty]` | Must be null or empty string |

### Equality
| Attribute | Description |
|-----------|-------------|
| `[Equal(value)]` | Must equal the given constant (numeric or string) |
| `[NotEqual(value)]` | Must not equal the given constant |

### String Length
| Attribute | Description |
|-----------|-------------|
| `[Length(min, max)]` | String length within `[min, max]` |
| `[MinLength(n)]` | String length ≥ n |
| `[MaxLength(n)]` | String length ≤ n |

### Comparison (numeric)
| Attribute | Description |
|-----------|-------------|
| `[LessThan(n)]` | Value < n |
| `[LessThanOrEqualTo(n)]` | Value ≤ n |
| `[GreaterThan(n)]` | Value > n |
| `[GreaterThanOrEqualTo(n)]` | Value ≥ n |
| `[ExclusiveBetween(min, max)]` | min < value < max |
| `[InclusiveBetween(min, max)]` | min ≤ value ≤ max |

### Format / Pattern
| Attribute | Description |
|-----------|-------------|
| `[Matches(pattern)]` | Value matches the given regex pattern |
| `[EmailAddress]` | Valid email address (zero-alloc internal implementation) |
| `[PrecisionScale(p, s)]` | Decimal fits within precision/scale (zero-alloc via `decimal.GetBits()`) |

### Enum
| Attribute | Description |
|-----------|-------------|
| `[IsInEnum]` | Numeric value is a defined member of the property's enum type |
| `[IsEnumName(typeof(TEnum))]` | String is a valid enum member name (case-sensitive) |

---

## 3. Rule Chaining ✅

Multiple attributes on the same property are independent rules — all are evaluated by default (continue mode). Decorate with `[StopOnFirstFailure]` to stop after the first failure on that property.

```csharp
// All three rules evaluated; all failures reported
[NotEmpty]
[MinLength(2)]
[MaxLength(100)]
public string Name { get; set; } = "";

// Stop after first failure — e.g. NotEmpty fires, MinLength/MaxLength skipped
[StopOnFirstFailure]
[NotEmpty]
[MinLength(2)]
[MaxLength(100)]
public string Code { get; set; } = "";
```

---

## 4. Custom Validators ✅

### 4.1 Predicate (`Must`) ✅

`[Must(nameof(MethodName))]` calls an instance method on the model with signature `bool MethodName(PropertyType value)`:

```csharp
[Must(nameof(StartsWithA))]
public string Name { get; set; } = "";

private bool StartsWithA(string value) =>
    value.StartsWith("A", StringComparison.Ordinal);
```

### 4.2 Hand-Written Validator (`ValidatorFor<T>`) ✅

For types you don't control (third-party or value types), implement `ValidatorFor<T>` directly:

```csharp
public class CoordinateValidator : ValidatorFor<Coordinate>
{
    public override ValidationResult Validate(Coordinate c)
    {
        var failures = new List<ValidationFailure>();
        if (c.Lat < -90 || c.Lat > 90)
            failures.Add(new ValidationFailure
            {
                PropertyName = "Lat",
                ErrorMessage = "Latitude must be between -90 and 90.",
                ErrorCode = "LAT_RANGE"
            });
        return new ValidationResult(failures.ToArray());
    }
}
```

Wire it in with `[ValidateWith]` (see §8).

### 4.3 Multi-Failure Custom Logic ⬜

Attribute-based hook for adding multiple failures from a single method — not yet implemented.

---

## 5. Error Message Configuration ✅

### 5.1 `Message` ✅

Every validation attribute accepts a `Message` named parameter:

```csharp
[GreaterThan(0, Message = "Age must be positive.")]
public int Age { get; set; }
```

### 5.2 Placeholders ✅

Placeholders are resolved at code-gen time — no runtime allocation:

| Placeholder | Applies to |
|-------------|------------|
| `{PropertyName}` | All validators |
| `{ComparisonValue}` | Comparison validators (`GreaterThan`, `Equal`, etc.) |
| `{MinLength}` / `{MaxLength}` | Length validators |
| `{From}` / `{To}` | Between validators (`InclusiveBetween`, `ExclusiveBetween`) |

`{PropertyValue}` (runtime value of the failing property) is ⬜ pending — requires runtime allocation.

### 5.3 `ErrorCode` ✅

Named parameter on every validation attribute:

```csharp
[EmailAddress(ErrorCode = "ERR_EMAIL_INVALID")]
public string Email { get; set; } = "";
```

Propagated through nested and collection validators.

### 5.4 `Severity` ✅

Named parameter on every validation attribute. Levels: `Error` (default), `Warning`, `Info`:

```csharp
[NotEmpty(Severity = Severity.Warning)]
public string MiddleName { get; set; } = "";
```

Propagated through nested and collection validators.

### 5.5 `WithName` / Display Name Override ⬜

Override the property name used in error messages — not yet implemented.

---

## 6. Conditional Validation ✅

### 6.1 `When` / `Unless` ✅

Named parameters on every validation attribute. Reference an instance method on the model with signature `bool MethodName()`:

```csharp
[NotNull(When = nameof(ShippingRequired))]
public Address? ShippingAddress { get; set; }
private bool ShippingRequired() => RequiresShipping;

[MinLength(5, Unless = nameof(ShortNameOk))]
public string Name { get; set; } = "";
private bool ShortNameOk() => AllowShortName;
```

`When` / `Unless` compose correctly with `[StopOnFirstFailure]`: a skipped rule (condition false) does not trigger the stop.

### 6.2 Cross-Property Condition Block ⬜

A single condition guard covering multiple properties — not yet implemented.

---

## 7. Cascade Modes ✅

### 7.1 Rule-Level Cascade ✅

- **Continue** (default) — all rules on a property run independently; all failures reported
- **Stop** — opt in via `[StopOnFirstFailure]`; stops at the first failure on that property

```csharp
[StopOnFirstFailure]
[NotNull]
[NotEmpty]
[MinLength(3)]
public string Username { get; set; } = "";
```

### 7.2 Validator-Level Cascade ⬜

Stop after the first *property* that produces a failure (fail-fast across properties) — not yet implemented.

### 7.3 Global Defaults ⬜

Configurable via `ValidatorOptions` — not yet implemented.

---

## 8. Complex Property Validation ✅

### Auto-detected nested validation ✅

If a property's type also has `[Validate]`, the generator injects the nested validator via constructor and runs it automatically. Failures are prefixed with the property name:

```csharp
[Validate]
public class Order
{
    [NotNull]
    public Address? ShippingAddress { get; set; }  // → "ShippingAddress.Street", etc.
}

// Generated constructor:
// OrderValidator(AddressValidator shippingAddressValidator) { ... }
```

### Explicit validator override (`[ValidateWith]`) ✅

Use `[ValidateWith(typeof(TValidator))]` for types you don't control or when you want to override the auto-detected validator:

```csharp
[Validate]
public class Location
{
    [ValidateWith(typeof(CoordinateValidator))]
    public Coordinate Point { get; set; } = new();
}
```

The generator produces **ZV0011** (warning) if `[ValidateWith]` is redundant (type already has `[Validate]`) and **ZV0012** (error) if the validator type doesn't implement `ValidatorFor<T>` for the correct type.

---

## 9. Collection Validation ✅

Properties of type `IEnumerable<T>` (including arrays and `List<T>`) whose element type has `[Validate]` are automatically validated per element. Null collections and null elements are silently skipped. Failures use bracket-index notation:

```csharp
[Validate]
public class Cart
{
    public List<LineItem> Items { get; set; } = [];
    // → "Items[0].Sku", "Items[2].Quantity", etc.
}
```

`[ValidateWith]` also works on collection properties — specify the element validator type.

### Index customization ⬜

Override how indices appear in failure property names — not yet implemented.

---

## 10. RuleSets ⬜

Group rules by name and execute selectively (e.g., "Create" vs "Update") — not yet implemented.

---

## 11. Dependent Rules ⬜

Run follow-up rules only when a preceding rule passes. In the attribute model, the natural equivalent would be `[StopOnFirstFailure]` (§3/§7.1) which stops the chain at the first failure. Cross-property dependency graphs are not yet implemented.

---

## 12. Inheritance / Polymorphic Validation ⬜

Dispatch to a type-specific validator based on the runtime type of a property — not yet implemented.

---

## 13. Async Validation ⬜

`ValidateAsync` and async predicate support are out of scope for the current zero-alloc design. Async validators inherently require heap allocation (state machines, `Task`). ASP.NET Core model binding is sync-only anyway.

---

## 14. Rule Inclusion / Reuse ⬜

Compose validators by including all rules from a base validator — not yet implemented.

---

## 15. Pre-Validation Hook ⬜

Override a `PreValidate` method on the generated validator to short-circuit validation before any rules run (e.g., null-model guard) — not yet implemented.

---

## 16. Root Context Data ⬜

Pass typed ambient data into the validation pipeline without changing the model signature — not yet implemented.

---

## 17. Dependency Injection ✅

### ASP.NET Core auto-registration ✅

`ZValidation.AspNetCore.Generator` auto-generates `AddZValidationAutoValidation()` which registers all validators as `Transient` and wires up an `IActionFilter` that validates action arguments and returns `422 UnprocessableEntity` + `ValidationProblemDetails` on failure:

```csharp
// Program.cs / Startup.cs
services.AddZValidationAutoValidation();
```

### ZInject integration ✅

For non-ASP.NET scenarios, decorate validators with a lifetime attribute; ZInject generates compile-time DI registration with no reflection:

```csharp
[Scoped]
public partial class CustomerValidator : ValidatorFor<Customer> { }

[Transient]
public partial class OrderValidator : ValidatorFor<Order> { }
```

---

## 18. Localization ⬜

Override default error messages globally and plug in resource-file providers — not yet implemented.

---

## 19. Test Extensions ✅

`ZValidation.Testing` provides `ValidationAssert` for clean xUnit assertions:

```csharp
ValidationAssert.NoErrors(validator.Validate(model));
ValidationAssert.HasError(result, "Email");
ValidationAssert.HasErrorWithMessage(result, "Email", "Invalid email address.");
```

---

## 20. Zero-Allocation Specifics

| Feature | Status |
|---------|--------|
| `readonly struct ValidationFailure` | ✅ |
| `ref struct ValidationContext<T>` | ✅ |
| Source-generated inline dispatch (no reflection) | ✅ |
| Flat-path preallocated array (no heap allocation per call) | ✅ |
| Compile-time placeholder substitution | ✅ |
| Zero-alloc email validation | ✅ |
| Zero-alloc decimal precision check (`decimal.GetBits()`) | ✅ |
| `ReadOnlySpan<ValidationFailure>` result exposure | ✅ |
| `Span<ValidationFailure>` / `stackalloc` for mixed-path | ⬜ (mixed-path still uses `List<T>`) |
| `ArrayPool<ValidationFailure>` for large result sets | ⬜ |
| `{PropertyValue}` placeholder (runtime value) | ⬜ (requires allocation) |
| AOT / NativeAOT safe | ✅ (no `Activator.CreateInstance`, no reflection) |

---

## 21. ASP.NET Core Integration ✅

- Auto-validates action arguments via a source-generated `IActionFilter`
- Returns `422 UnprocessableEntity` + `ValidationProblemDetails` on failure
- Source-generated `AddZValidationAutoValidation()` extension method registers all validators and the filter
- Validators resolved from `IServiceProvider` (DI-friendly)
- `IValidateOptions<T>` integration ⬜ (not yet implemented)

---

## 22. Analyzers ✅

ZValidation enforces correctness and zero-allocation constraints at compile time via a curated set of Roslyn analyzers. All are analyzer-only dependencies (no runtime impact).

| Package | Purpose |
|---------|---------|
| `ZeroAlloc.Analyzers` | Detects allocation patterns (boxing, closures, LINQ, etc.) |
| `Meziantou.Analyzer` | General correctness, performance, and API usage |
| `Roslynator.Analyzers` | Code quality and style |
| `ErrorProne.NET.CoreAnalyzers` | Common correctness mistakes (unused results, exception handling) |
| `ErrorProne.NET.Structs` | Safe `struct` usage — defensive copies, missing `readonly` |
| `NetFabric.Hyperlinq.Analyzer` | LINQ patterns that should use zero-allocation enumeration |

In addition, `ZValidation.Generator` emits its own diagnostics:

| Code | Severity | Description |
|------|----------|-------------|
| ZV0011 | Warning | `[ValidateWith]` is redundant — property type already has `[Validate]` |
| ZV0012 | Error | `[ValidateWith]` validator type does not implement `ValidatorFor<T>` for the property type |

---

## Out of Scope (for now)

- Blazor integration
- Async validation (conflicts with zero-alloc design goal)
