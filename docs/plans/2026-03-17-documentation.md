# Documentation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Produce a root `README.md` and ten Docusaurus-ready Markdown files under `docs/` covering every feature of ZeroAlloc.Validation for external consumers.

**Architecture:** Plain Markdown with full YAML frontmatter (`id`, `title`, `slug`, `description`, `sidebar_position`) on every `docs/` file. Mermaid fenced blocks for all diagrams. No MDX. No code is modified — documentation only.

**Tech Stack:** Markdown, YAML frontmatter, Mermaid diagrams, BenchmarkDotNet results from `benchmarks/ZeroAlloc.Validation.Benchmarks/BenchmarkDotNet.Artifacts/results/`.

---

## Key facts for every task

- Package names: `ZeroAlloc.Validation`, `ZeroAlloc.Validation.AspNetCore`, `ZeroAlloc.Validation.Testing`
- Generator is a Roslyn incremental source generator — no runtime reflection
- `ValidatorFor<T>` is the generated base class; `Validate(T instance)` returns `ValidationResult`
- `ValidationResult` — `bool IsValid`, `ReadOnlySpan<ValidationFailure> Failures`
- `ValidationFailure` — `string PropertyName`, `string ErrorMessage`, `string? ErrorCode`, `Severity Severity`
- `Severity` enum — `Error` (default/0), `Warning`, `Info`
- `ValidationAttribute` base — shared properties: `Message`, `When`, `Unless`, `ErrorCode`, `Severity`
- ASP.NET Core: filter returns HTTP 422 `ValidationProblemDetails` on failure
- `AddZValidationAutoValidation()` — registers all validators as Transient + adds `ZValidationActionFilter`
- DI lifetime on validators: `[Transient]`, `[Scoped]`, `[Singleton]` attributes on the model class

---

## Task 1: Root README

**Files:**
- Create: `README.md`

**Step 1: Write the file**

```markdown
---
(no frontmatter on the root README)
---
```

Content sections in order:

1. **Headline** — `# ZeroAlloc.Validation`
2. **Badges** — NuGet (placeholder `[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Validation)](https://www.nuget.org/packages/ZeroAlloc.Validation)`), build, license
3. **Pitch** — 2-3 sentences: source-generated, attribute-based, zero-allocation validation for .NET. No reflection at runtime. Valid-path allocates nothing.
4. **Install**
   ```bash
   dotnet add package ZeroAlloc.Validation
   ```
5. **30-second example**
   ```csharp
   using ZeroAlloc.Validation;

   [Validate]
   public class CreateOrderRequest
   {
       [NotEmpty][MaxLength(50)] public string  Reference { get; set; } = "";
       [GreaterThan(0)]          public decimal Amount    { get; set; }
       [NotEmpty][EmailAddress]  public string  Email     { get; set; } = "";
   }

   // Usage (validator is source-generated as CreateOrderRequestValidator)
   var validator = new CreateOrderRequestValidator();
   var result    = validator.Validate(request);

   if (!result.IsValid)
       foreach (ref readonly var f in result.Failures)
           Console.WriteLine($"{f.PropertyName}: {f.ErrorMessage}");
   ```
6. **Performance** — condensed table (valid path only, from benchmark results):
   | Scenario         | ZeroAlloc.Validation | FluentValidation | Speedup | Allocation |
   |------------------|---------------------:|-----------------:|:-------:|:----------:|
   | Flat model       |              6.7 ns  |         327 ns   |  ~49×   |    0 B     |
   | Nested model     |             10.1 ns  |         619 ns   |  ~61×   |    0 B     |
   | Collection (3×)  |             14.3 ns  |        2,043 ns  | ~143×   |    0 B     |
   Link: `See [Performance](docs/performance.md) for full results.`
7. **Features** — bullet list:
   - Zero heap allocation on the valid path
   - 25+ built-in validation attributes
   - Nested object and collection validation
   - ASP.NET Core auto-validation (HTTP 422 on failure)
   - Per-rule severity (`Error`, `Warning`, `Info`)
   - Conditional rules (`When` / `Unless` / `[SkipWhen]`)
   - Short-circuit with `[StopOnFirstFailure]`
   - Custom rules via `[Must]` predicates or `[CustomValidation]` methods
   - Testing helpers via `ZeroAlloc.Validation.Testing`
8. **Links** — Getting Started, full docs site (placeholder URL)

**Step 2: Verify**

Read the file. Confirm all sections present, code blocks render correctly, badge links are syntactically valid.

**Step 3: Commit**

```bash
git add README.md
git commit -m "docs: add root README with pitch, examples, and performance table"
```

---

## Task 2: Getting Started

**Files:**
- Create: `docs/getting-started.md`

**Step 1: Write the file**

```markdown
---
id: getting-started
title: Getting Started
slug: /docs/getting-started
description: Install ZeroAlloc.Validation, annotate your first model, and validate it in three steps.
sidebar_position: 1
---
```

Sections:

1. **Installation** — three packages, each with `dotnet add package` snippet:
   - `ZeroAlloc.Validation` (core)
   - `ZeroAlloc.Validation.AspNetCore` (optional, ASP.NET Core auto-validation)
   - `ZeroAlloc.Validation.Testing` (optional, test helpers)

2. **How it works** — Mermaid flowchart:
   ```mermaid
   flowchart LR
       A["Your model\n[Validate] + attributes"] -->|"build time"| B["Source Generator\n(Roslyn)"]
       B --> C["Generated\nMyModelValidator.cs"]
       C -->|"runtime call"| D["validator.Validate(instance)"]
       D --> E["ValidationResult\n.IsValid / .Failures"]
   ```

3. **Annotate your model** — add `[Validate]` + attribute examples:
   ```csharp
   using ZeroAlloc.Validation;

   [Validate]
   public class RegisterUserRequest
   {
       [NotEmpty][MaxLength(100)] public string Username { get; set; } = "";
       [NotEmpty][MinLength(8)]   public string Password { get; set; } = "";
       [NotEmpty][EmailAddress]   public string Email    { get; set; } = "";
   }
   ```

4. **Call the validator** — the generator creates `RegisterUserRequestValidator` in the same namespace:
   ```csharp
   var validator = new RegisterUserRequestValidator();
   var result    = validator.Validate(new RegisterUserRequest
   {
       Username = "",
       Password = "abc",
       Email    = "not-an-email"
   });

   Console.WriteLine(result.IsValid); // false

   foreach (ref readonly var failure in result.Failures)
       Console.WriteLine($"[{failure.PropertyName}] {failure.ErrorMessage}");
   // [Username] 'Username' must not be empty.
   // [Password] 'Password' must be at least 8 characters.
   // [Email] 'Email' is not a valid email address.
   ```

5. **What was generated** — brief prose: the generator emits a `partial class` that extends `ValidatorFor<T>`. No reflection, no expression trees — pure IL at build time.

6. **Next steps** — links to attributes.md, nested-validation.md, aspnetcore.md

**Step 2: Verify**

Read the file. Mermaid block is correct syntax. Code compiles conceptually (correct types used).

**Step 3: Commit**

```bash
git add docs/getting-started.md
git commit -m "docs: add getting-started page"
```

---

## Task 3: Attribute Reference

**Files:**
- Create: `docs/attributes.md`

**Step 1: Write the file**

Frontmatter:
```yaml
---
id: attributes
title: Attribute Reference
slug: /docs/attributes
description: Complete reference for all built-in validation attributes in ZeroAlloc.Validation.
sidebar_position: 2
---
```

Opening paragraph: all validation attributes live in the `ZeroAlloc.Validation` namespace and inherit from `ValidationAttribute`. Every attribute exposes shared properties: `Message` (override error text), `ErrorCode`, `Severity`, `When` (method name for conditional rule), `Unless`.

Then a **reference table per category**, followed by a per-attribute code snippet.

**String attributes:**

| Attribute | Applies to | Description |
|---|---|---|
| `[NotEmpty]` | `string`, collections | Must not be null or empty/whitespace |
| `[Empty]` | `string`, collections | Must be null or empty |
| `[MaxLength(n)]` | `string` | Length ≤ n |
| `[MinLength(n)]` | `string` | Length ≥ n |
| `[Length(min, max)]` | `string` | min ≤ length ≤ max |
| `[Matches(pattern)]` | `string` | Must match regex pattern |
| `[EmailAddress]` | `string` | Must be a valid email |

**Numeric / comparison attributes:**

| Attribute | Applies to | Description |
|---|---|---|
| `[GreaterThan(value)]` | numeric | > value |
| `[GreaterThanOrEqualTo(value)]` | numeric | ≥ value |
| `[LessThan(value)]` | numeric | < value |
| `[LessThanOrEqualTo(value)]` | numeric | ≤ value |
| `[InclusiveBetween(min, max)]` | numeric | min ≤ x ≤ max |
| `[ExclusiveBetween(min, max)]` | numeric | min < x < max |
| `[Equal(value)]` | any | == value |
| `[NotEqual(value)]` | any | != value |
| `[PrecisionScale(p, s)]` | `decimal` | At most p digits total, s after decimal |

**Null / existence:**

| Attribute | Applies to | Description |
|---|---|---|
| `[NotNull]` | reference types, nullable | Must not be null (also triggers nested validation) |
| `[Null]` | reference types, nullable | Must be null |

**Enum attributes:**

| Attribute | Applies to | Description |
|---|---|---|
| `[IsEnumName]` | `string` | Must be a defined name in a given enum |
| `[IsInEnum]` | numeric/enum | Must be a defined value in the enum |

**Behaviour attributes (on properties):**

| Attribute | Applies to | Description |
|---|---|---|
| `[StopOnFirstFailure]` | property | Stop checking further rules on this property after the first failure |

**Model-level attributes (on class):**

| Attribute | Applies to | Description |
|---|---|---|
| `[Validate]` | class | Marks the class for source generation |
| `[SkipWhen(methodName)]` | class | Skip all validation when a static bool method returns true |
| `[Transient]` / `[Scoped]` / `[Singleton]` | class | DI lifetime for the generated validator |

**Custom rule attributes (on property):**

| Attribute | Applies to | Description |
|---|---|---|
| `[Must(nameof(Method))]` | property | Call an instance bool method on the model |
| `[ValidateWith(typeof(TValidator))]` | nested/collection property | Override which validator is used |

Include a code snippet for each category showing two or three attributes in action.

**Step 2: Verify**

Read the file. Tables are syntactically correct Markdown.

**Step 3: Commit**

```bash
git add docs/attributes.md
git commit -m "docs: add attribute reference page"
```

---

## Task 4: Nested Validation

**Files:**
- Create: `docs/nested-validation.md`

**Step 1: Write the file**

Frontmatter:
```yaml
---
id: nested-validation
title: Nested Validation
slug: /docs/nested-validation
description: Validate nested objects automatically or with an explicit validator override.
sidebar_position: 3
---
```

Sections:

1. **Automatic nesting** — when a property is annotated `[NotNull]` and its type also carries `[Validate]`, the generator automatically calls the nested validator and prefixes failures with `"ParentProp."`.

   ```csharp
   [Validate]
   public class Address
   {
       [NotEmpty] public string Street     { get; set; } = "";
       [NotEmpty] public string City       { get; set; } = "";
       [NotEmpty] public string PostalCode { get; set; } = "";
   }

   [Validate]
   public class Order
   {
       [NotEmpty]           public string   Reference       { get; set; } = "";
       [NotNull]            public Address? ShippingAddress { get; set; }
   }
   ```

   Failure example:
   ```
   PropertyName: "ShippingAddress.Street"
   ErrorMessage: "'Street' must not be empty."
   ```

2. **Failure accumulation diagram** — Mermaid:
   ```mermaid
   flowchart TD
       A["OrderValidator.Validate(order)"] --> B{ShippingAddress null?}
       B -- "yes" --> C["Failure: 'ShippingAddress' must not be null"]
       B -- "no" --> D["AddressValidator.Validate(address)"]
       D --> E["Failures prefixed\nShippingAddress.Street\nShippingAddress.City\n..."]
       C --> F["ValidationResult"]
       E --> F
   ```

3. **Explicit override with `[ValidateWith]`** — use when the nested type is from a third-party library or does not carry `[Validate]`:

   ```csharp
   // Third-party type — cannot add [Validate]
   public class ExternalAddress { public string Line1 { get; set; } = ""; }

   // Write your own validator
   public class ExternalAddressValidator : ValidatorFor<ExternalAddress> { ... }

   [Validate]
   public class Order
   {
       [NotNull]
       [ValidateWith(typeof(ExternalAddressValidator))]
       public ExternalAddress? ShippingAddress { get; set; }
   }
   ```

4. **Rules for nesting:**
   - `[NotNull]` alone validates null-ness; the nested validator only runs when the value is non-null
   - `[ValidateWith]` overrides whichever validator would have been used automatically
   - Nesting is unlimited depth — each level's validator is generated independently

**Step 2: Verify**

Read the file. Mermaid flowchart is valid syntax. Code examples use correct attribute names.

**Step 3: Commit**

```bash
git add docs/nested-validation.md
git commit -m "docs: add nested validation page"
```

---

## Task 5: Collection Validation

**Files:**
- Create: `docs/collection-validation.md`

**Step 1: Write the file**

Frontmatter:
```yaml
---
id: collection-validation
title: Collection Validation
slug: /docs/collection-validation
description: Validate every element of a List<T>, array, or IEnumerable<T> property automatically.
sidebar_position: 4
---
```

Sections:

1. **Basic usage** — annotate the collection property with `[ValidateWith(typeof(TValidator))]` or rely on automatic detection when the element type carries `[Validate]`:

   ```csharp
   [Validate]
   public class LineItem
   {
       [NotEmpty]       public string Sku      { get; set; } = "";
       [GreaterThan(0)] public int    Quantity  { get; set; }
   }

   [Validate]
   public class Cart
   {
       [NotEmpty] public string          CartId { get; set; } = "";
       public List<LineItem>             Items  { get; set; } = [];
   }
   ```

2. **Index-prefixed property paths** — failures include `[index]` in `PropertyName`:

   ```
   Items[0].Sku      → "'Sku' must not be empty."
   Items[1].Quantity → "'Quantity' must be greater than 0."
   ```

3. **Per-element iteration diagram** — Mermaid:
   ```mermaid
   sequenceDiagram
       participant CV as CartValidator
       participant LV as LineItemValidator
       CV->>CV: Validate CartId
       loop for each Items[i]
           CV->>LV: Validate(items[i])
           LV-->>CV: failures (prefixed Items[i].*)
       end
       CV-->>caller: ValidationResult
   ```

4. **Using `[ValidateWith]` on collections** — explicit validator override for element types you don't own:
   ```csharp
   [ValidateWith(typeof(ExternalProductValidator))]
   public List<ExternalProduct> Products { get; set; } = [];
   ```

5. **Supported collection types** — `List<T>`, `T[]`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`.

**Step 2: Verify**

Read the file. Sequence diagram is valid Mermaid syntax.

**Step 3: Commit**

```bash
git add docs/collection-validation.md
git commit -m "docs: add collection validation page"
```

---

## Task 6: Custom Validation

**Files:**
- Create: `docs/custom-validation.md`

**Step 1: Write the file**

Frontmatter:
```yaml
---
id: custom-validation
title: Custom Validation
slug: /docs/custom-validation
description: Add custom business rules with [Must] predicates or [CustomValidation] methods.
sidebar_position: 5
---
```

Sections:

1. **`[Must(nameof(Method))]`** — simplest option, an instance bool method on the model:

   ```csharp
   [Validate]
   public class PasswordChange
   {
       [NotEmpty]
       public string NewPassword { get; set; } = "";

       [NotEmpty]
       [Must(nameof(MatchesNewPassword))]
       public string ConfirmPassword { get; set; } = "";

       private bool MatchesNewPassword(string confirm)
           => confirm == NewPassword;
   }
   ```

   The method signature must be `bool MethodName(TPropType value)` where `TPropType` matches the property type.

2. **`[CustomValidation]` method** — for more control, mark a `static` method on the model with `[CustomValidation]`. The generator calls it and includes any returned failures:

   ```csharp
   [Validate]
   public class DateRange
   {
       public DateTime From { get; set; }
       public DateTime To   { get; set; }

       [CustomValidation]
       private static IEnumerable<ValidationFailure> ValidateDateOrder(DateRange instance)
       {
           if (instance.From >= instance.To)
               yield return new ValidationFailure
               {
                   PropertyName = nameof(From),
                   ErrorMessage = "'From' must be earlier than 'To'."
               };
       }
   }
   ```

   Method signature: `static IEnumerable<ValidationFailure> AnyName(TModel instance)`.

3. **Overriding error messages on any attribute** — the `Message` property on `ValidationAttribute`:
   ```csharp
   [NotEmpty(Message = "Please provide a reference number.")]
   public string Reference { get; set; } = "";
   ```

4. **Custom `ErrorCode`**:
   ```csharp
   [GreaterThan(0, ErrorCode = "AMOUNT_POSITIVE")]
   public decimal Amount { get; set; }
   ```

**Step 2: Verify**

Read the file. Method signatures match what the generator expects.

**Step 3: Commit**

```bash
git add docs/custom-validation.md
git commit -m "docs: add custom validation page"
```

---

## Task 7: Error Messages

**Files:**
- Create: `docs/error-messages.md`

**Step 1: Write the file**

Frontmatter:
```yaml
---
id: error-messages
title: Error Messages
slug: /docs/error-messages
description: Understand default error message formats, override them, and use display names.
sidebar_position: 6
---
```

Sections:

1. **`ValidationFailure` structure:**
   ```csharp
   public readonly struct ValidationFailure
   {
       public string    PropertyName { get; init; }
       public string    ErrorMessage { get; init; }
       public string?   ErrorCode    { get; init; }
       public Severity  Severity     { get; init; }
   }
   ```

2. **Default message format** — `"'PropertyName' must …"`. Table of defaults per attribute:

   | Attribute | Default message |
   |---|---|
   | `[NotEmpty]` | `'X' must not be empty.` |
   | `[NotNull]` | `'X' must not be null.` |
   | `[MaxLength(n)]` | `'X' must be at most n characters.` |
   | `[MinLength(n)]` | `'X' must be at least n characters.` |
   | `[EmailAddress]` | `'X' is not a valid email address.` |
   | `[GreaterThan(n)]` | `'X' must be greater than n.` |
   | `[LessThan(n)]` | `'X' must be less than n.` |
   | `[Matches(p)]` | `'X' is not in the correct format.` |
   | … | … |

3. **`[DisplayName]`** — override the property label used in messages:
   ```csharp
   [DisplayName("Email Address")]
   [NotEmpty][EmailAddress]
   public string Email { get; set; } = "";
   // Failure: "'Email Address' is not a valid email address."
   ```

4. **`Message` override** — per-attribute inline override:
   ```csharp
   [NotEmpty(Message = "Please enter your email.")]
   public string Email { get; set; } = "";
   ```

5. **`Severity`** — `Error` (default), `Warning`, `Info`. Set per attribute:
   ```csharp
   [Matches(@"^\+?[0-9\s\-()]+$", Severity = Severity.Warning)]
   public string Phone { get; set; } = "";
   ```
   Filter by severity at the call site:
   ```csharp
   var errors = result.Failures.ToArray()
       .Where(f => f.Severity == Severity.Error);
   ```

6. **`ErrorCode`** — machine-readable code alongside the human message:
   ```csharp
   [GreaterThan(0, ErrorCode = "AMOUNT_POSITIVE")]
   public decimal Amount { get; set; }
   // failure.ErrorCode == "AMOUNT_POSITIVE"
   ```

**Step 2: Verify**

Read the file. All `ValidationFailure` field names match the actual struct (`PropertyName`, `ErrorMessage`, `ErrorCode`, `Severity`).

**Step 3: Commit**

```bash
git add docs/error-messages.md
git commit -m "docs: add error messages page"
```

---

## Task 8: ASP.NET Core Integration

**Files:**
- Create: `docs/aspnetcore.md`

**Step 1: Write the file**

Frontmatter:
```yaml
---
id: aspnetcore
title: ASP.NET Core Integration
slug: /docs/aspnetcore
description: Automatically validate action parameters and return HTTP 422 on failure with zero boilerplate.
sidebar_position: 7
---
```

Sections:

1. **Install**
   ```bash
   dotnet add package ZeroAlloc.Validation.AspNetCore
   ```

2. **Register** — one call in `Program.cs`:
   ```csharp
   builder.Services.AddZValidationAutoValidation();
   ```
   This registers `ZValidationActionFilter` as an MVC global filter and registers every generated validator as `Transient`.

3. **Request flow diagram** — Mermaid:
   ```mermaid
   sequenceDiagram
       participant Client
       participant Filter as ZValidationActionFilter
       participant Validator as MyModelValidator
       participant Controller

       Client->>Filter: POST /orders (body: CreateOrderRequest)
       Filter->>Validator: Validate(request)
       alt IsValid
           Validator-->>Filter: ValidationResult { IsValid = true }
           Filter->>Controller: OnActionExecuting (pass through)
           Controller-->>Client: 200 OK
       else Invalid
           Validator-->>Filter: ValidationResult { IsValid = false }
           Filter-->>Client: 422 ValidationProblemDetails
       end
   ```

4. **Response shape** — the filter returns HTTP 422 with `ValidationProblemDetails`:
   ```json
   {
     "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
     "title": "One or more validation errors occurred.",
     "status": 422,
     "errors": {
       "Reference": ["'Reference' must not be empty."],
       "Amount": ["'Amount' must be greater than 0."]
     }
   }
   ```

5. **DI lifetime on validators** — annotate the model class to control its validator's lifetime:
   ```csharp
   [Validate]
   [Scoped]   // generated validator registered as Scoped instead of Transient
   public class CreateOrderRequest { ... }
   ```
   Valid options: `[Transient]` (default), `[Scoped]`, `[Singleton]`.

6. **What gets generated** — brief note: the AspNetCore generator emits `ZValidationActionFilter` (a type-switch dispatch over all `[Validate]` models) and `ZValidationServiceCollectionExtensions` containing `AddZValidationAutoValidation()`.

**Step 2: Verify**

Read the file. Sequence diagram is valid. JSON example matches actual `ValidationProblemDetails` shape. Extension method name is `AddZValidationAutoValidation`.

**Step 3: Commit**

```bash
git add docs/aspnetcore.md
git commit -m "docs: add ASP.NET Core integration page"
```

---

## Task 9: Testing

**Files:**
- Create: `docs/testing.md`

**Step 1: Write the file**

Frontmatter:
```yaml
---
id: testing
title: Testing
slug: /docs/testing
description: Write concise, assertion-first validator tests with the ZeroAlloc.Validation.Testing helpers.
sidebar_position: 8
---
```

Sections:

1. **Install**
   ```bash
   dotnet add package ZeroAlloc.Validation.Testing
   ```

2. **`ValidationAssert` methods:**

   | Method | Behaviour |
   |---|---|
   | `ValidationAssert.NoErrors(result)` | Throws if `result.IsValid == false` |
   | `ValidationAssert.HasError(result, "PropertyName")` | Throws if no failure found for that property |
   | `ValidationAssert.HasErrorWithMessage(result, "PropertyName", "message")` | Throws if no matching property+message failure |

3. **Flat model test example:**
   ```csharp
   public class OrderValidatorTests
   {
       private readonly OrderValidator _sut = new();

       [Fact]
       public void Valid_order_passes()
       {
           var result = _sut.Validate(new Order
           {
               Reference = "ORD-001",
               Amount    = 99.99m,
               Email     = "customer@example.com"
           });
           ValidationAssert.NoErrors(result);
       }

       [Fact]
       public void Empty_reference_fails()
       {
           var result = _sut.Validate(new Order { Reference = "", Amount = 1m, Email = "a@b.com" });
           ValidationAssert.HasError(result, nameof(Order.Reference));
       }

       [Fact]
       public void Invalid_email_has_expected_message()
       {
           var result = _sut.Validate(new Order { Reference = "X", Amount = 1m, Email = "bad" });
           ValidationAssert.HasErrorWithMessage(result, nameof(Order.Email),
               "'Email' is not a valid email address.");
       }
   }
   ```

4. **Nested model test example:**
   ```csharp
   [Fact]
   public void Missing_shipping_address_fails_on_nested_property()
   {
       var result = _sut.Validate(new OrderWithAddress
       {
           Reference       = "ORD-001",
           ShippingAddress = new Address { Street = "", City = "Amsterdam", PostalCode = "1234AB" }
       });
       ValidationAssert.HasError(result, "ShippingAddress.Street");
   }
   ```

5. **Tip** — inject validators via their generated type; no interface wiring required for tests.

**Step 2: Verify**

Read the file. `ValidationAssert` method signatures match the actual implementation (`HasError`, `HasErrorWithMessage`, `NoErrors`).

**Step 3: Commit**

```bash
git add docs/testing.md
git commit -m "docs: add testing page"
```

---

## Task 10: Performance

**Files:**
- Create: `docs/performance.md`
- Reference: `benchmarks/ZeroAlloc.Validation.Benchmarks/BenchmarkDotNet.Artifacts/results/*.md`

**Step 1: Write the file**

Frontmatter:
```yaml
---
id: performance
title: Performance
slug: /docs/performance
description: Zero heap allocation on the valid path — how it works and what the benchmarks show.
sidebar_position: 9
---
```

Sections:

1. **Design goal** — no heap allocation when the model is valid. This matters in hot paths (API request validation, event processing).

2. **How zero allocation is achieved** — lazy-allocation pattern in the generated flat-path code:

   ```csharp
   // Generated code (simplified)
   ValidationFailure[]? _buf  = null;   // NOT allocated upfront
   int                  _count = 0;

   // Per rule:
   if (/* rule fails */)
   {
       _buf ??= new ValidationFailure[totalRules];  // only on first failure
       _buf[_count++] = new ValidationFailure { ... };
   }

   // Terminal:
   if (_count == 0)
       return new ValidationResult(Array.Empty<ValidationFailure>()); // static singleton
   ```

3. **Why not `stackalloc`** — `ValidationFailure` contains `string` reference fields and therefore does not satisfy the `unmanaged` constraint required for `stackalloc<T>`. Lazy heap allocation is the next-best option.

4. **Benchmark environment:**
   ```
   BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
   .NET 10.0.4 (10.0.426.12010), X64 RyuJIT AVX2
   ```

5. **Flat model results:**

   | Method     | Mean         | Ratio | Allocated | Alloc Ratio |
   |----------- |-------------:|------:|----------:|------------:|
   | ZA_Valid   |     6.713 ns |  0.02 |         - |        0.00 |
   | ZA_Invalid |    44.012 ns |  0.14 |     304 B |        0.46 |
   | FV_Valid   |   327.269 ns |  1.01 |     664 B |        1.00 |
   | FV_Invalid | 2,462.893 ns |  7.58 |    5,408 B |        8.14 |

6. **Nested model results:**

   | Method     | Mean        | Ratio | Allocated | Alloc Ratio |
   |----------- |------------:|------:|----------:|------------:|
   | ZA_Valid   |    10.09 ns |  0.02 |         - |        0.00 |
   | ZA_Invalid |    96.56 ns |  0.16 |     608 B |        0.41 |
   | FV_Valid   |   619.14 ns |  1.00 |    1,488 B |        1.00 |
   | FV_Invalid | 2,974.10 ns |  4.82 |    6,328 B |        4.25 |

7. **Collection model results (3 items):**

   | Method     | Mean        | Ratio | Allocated | Alloc Ratio |
   |----------- |------------:|------:|----------:|------------:|
   | ZA_Valid   |    14.30 ns | 0.007 |         - |        0.00 |
   | ZA_Invalid |   178.54 ns | 0.089 |     856 B |        0.25 |
   | FV_Valid   | 2,042.95 ns | 1.014 |    3,456 B |        1.00 |
   | FV_Invalid | 5,957.29 ns | 2.958 |   11,568 B |        3.35 |

8. **Running the benchmarks yourself:**
   ```bash
   # From repo root
   dotnet build benchmarks/ZeroAlloc.Validation.Benchmarks -c Release --no-incremental
   cd benchmarks/ZeroAlloc.Validation.Benchmarks
   dotnet run -c Release -- --filter '*' --job Default
   ```

**Step 2: Verify**

Read the file. Numbers match the artifact files at `benchmarks/ZeroAlloc.Validation.Benchmarks/BenchmarkDotNet.Artifacts/results/`.

**Step 3: Commit**

```bash
git add docs/performance.md
git commit -m "docs: add performance page with benchmark results"
```

---

## Task 11: Advanced Features

**Files:**
- Create: `docs/advanced.md`

**Step 1: Write the file**

Frontmatter:
```yaml
---
id: advanced
title: Advanced Features
slug: /docs/advanced
description: Conditional rules, short-circuit validation, per-rule severity, and DI lifetime control.
sidebar_position: 10
---
```

Sections:

1. **`[SkipWhen]`** — skip all validation on the model when a static method returns `true`:
   ```csharp
   [Validate]
   [SkipWhen(nameof(IsDraft))]
   public class Order
   {
       public bool IsDraft { get; set; }

       [NotEmpty] public string Reference { get; set; } = "";
       [GreaterThan(0)] public decimal Amount { get; set; }

       private static bool IsDraft(Order o) => o.IsDraft;
   }
   ```
   When `IsDraft` returns `true`, the validator returns `IsValid = true` without evaluating any rules.

2. **`When` / `Unless` on individual attributes** — per-rule conditions:
   ```csharp
   [Validate]
   public class Payment
   {
       public bool IsRecurring { get; set; }

       // Only validate CardToken when NOT recurring
       [NotEmpty(Unless = nameof(IsRecurring))]
       public string? CardToken { get; set; }
   }
   ```
   `When = "MethodName"` — run rule only when the named bool method returns `true`.
   `Unless = "MethodName"` — run rule only when the named bool method returns `false`.
   Method signature: `bool MethodName()` (instance, no parameters) on the model.

3. **`[StopOnFirstFailure]`** — stop evaluating further rules on a property after the first failure:
   ```csharp
   [Validate]
   public class User
   {
       [StopOnFirstFailure]
       [NotEmpty]
       [MinLength(3)]
       [MaxLength(50)]
       public string Username { get; set; } = "";
   }
   ```
   Without `[StopOnFirstFailure]`, all three rules run independently. With it, if `[NotEmpty]` fails, `[MinLength]` and `[MaxLength]` are skipped.

4. **Severity levels** — three levels: `Error` (default), `Warning`, `Info`. Set per attribute:
   ```csharp
   [NotEmpty]
   [Matches(@"^\+?[0-9\s\-()]+$", Severity = Severity.Warning,
            Message = "Phone number format looks unusual.")]
   public string Phone { get; set; } = "";
   ```
   Filter at the call site:
   ```csharp
   var hasErrors = result.Failures.ToArray()
       .Any(f => f.Severity == Severity.Error);
   ```

5. **DI lifetime attributes** — `[Transient]` (default), `[Scoped]`, `[Singleton]` on the model class:
   ```csharp
   [Validate]
   [Singleton]   // validator is registered as a singleton — use when validator is stateless
   public class AppConfig { ... }
   ```

**Step 2: Verify**

Read the file. `SkipWhen` and `StopOnFirstFailure` attribute usages match the actual attribute signatures (`[AttributeUsage(AttributeTargets.Class)]` and `[AttributeUsage(AttributeTargets.Property)]` respectively).

**Step 3: Commit**

```bash
git add docs/advanced.md
git commit -m "docs: add advanced features page"
```

---

## Task 12: Final commit

After all 11 tasks complete, verify all files exist:

```bash
ls README.md docs/getting-started.md docs/attributes.md docs/nested-validation.md \
   docs/collection-validation.md docs/custom-validation.md docs/error-messages.md \
   docs/aspnetcore.md docs/testing.md docs/performance.md docs/advanced.md
```

No final commit needed — each task committed individually.
