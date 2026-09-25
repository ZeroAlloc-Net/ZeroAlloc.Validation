---
id: custom-validation
title: Custom Validation
slug: /docs/custom-validation
description: Reusable rule attributes with ValidationAttribute<T>, inline predicates with [Must], and cross-property rules with [CustomValidation].
sidebar_position: 5
---

# Custom Validation

ZeroAlloc.Validation provides three customization mechanisms for rules that go beyond the built-in attributes.

| Mechanism | Placement | Scope |
|---|---|---|
| `ValidationAttribute<T>` | Reusable attribute class | Single-property predicate, reusable across models |
| `[Must]` | Property | Single-property predicate, model-specific |
| `[CustomValidation]` | Instance method | Full model access (cross-property) |

---

## Custom rule attributes — ValidationAttribute\<T\>

Derive from `ValidationAttribute<T>` to define a reusable, named rule attribute, such as
`[NotBlank]` or `[MinWords(3)]`, instead of repeating a `[Must]` predicate method on every
model that needs the same check.

```csharp
[RuleMessage("{PropertyName} must not be blank.")]
public sealed class NotBlankAttribute : ValidationAttribute<string?>
{
    public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
}

[RuleMessage("{PropertyName} must have at least {minWords} words.")]
public sealed class MinWordsAttribute(int minWords) : ValidationAttribute<string?>
{
    public int MinWords { get; } = minWords;

    public override bool IsValid(string? value) => CountWords(value) >= minWords;

    private static int CountWords(string? value) => /* ... */ 0;
}

[Validate]
public class Article
{
    [NotBlank]
    public string? Author { get; set; }

    [MinWords(3)]
    public string? Title { get; set; }
}
```

`ValidationAttribute<T>` declares one member: `abstract bool IsValid(T value)`. Everything
else — `Message`, `ErrorCode`, `Severity`, `When`, `Unless`, and stop-on-first-failure — is
inherited from `ValidationAttribute` and works exactly as it does for a built-in rule.

### How the generator emits it

Each usage is rebuilt once as a `private static readonly` field on the generated validator
(named after the pattern `__Rule_{PropertyName}_{index}`), initialized from the attribute
arguments written on the usage, for example `new global::Ns.MinWordsAttribute(3)`. The
generated check is a statically bound call, `!field.IsValid(rawValue)` — there is no
reflection and no `Activator` involved.

**Allocation and AOT:** one instance is created per usage, when the validator type
initializes — not per validation call. A passing `Validate()` call allocates nothing because
of a custom rule, with two exceptions:

- A value-type property checked by a rule whose `T` is a reference type, such as `object` or
  an interface, is boxed on every call. A rule declared as `ValidationAttribute<object?>` on
  an `int` property boxes the `int` each time.
- A user-defined implicit conversion from the property type to `T` runs on every call, along
  with anything it allocates.

Declare `T` as the property's own type to avoid both. A failing call allocates its result,
the same as it does for a built-in rule. There is no reflection or `Activator`, so custom
rules are safe under trimming and NativeAOT, the same as every built-in rule.

**The instance is shared.** One static instance per usage serves every validation, on every
thread, so `IsValid` must be stateless and thread-safe: do not cache the last value or keep
counters in fields. The instance is built by the validator's type initializer, so a rule
constructor that throws surfaces as a `TypeInitializationException` from the validator.

**Apply the rule to a property.** The generator reads rules from properties only. A rule
attribute that widens its own `[AttributeUsage]` can compile on a field or a constructor
parameter, where it would never run, so that usage fails the build with
[ZV0024](diagnostics.md#zv0024). On a record's positional parameter, write the rule with the
`property:` target, `record Request([property: NotBlank] string? Name)`, so it lands on the
generated property.

**The property type must convert to the rule's `T` implicitly.** `[NotBlank]` above is
`ValidationAttribute<string?>`, so it can decorate a `string?` property. A property whose type
has no implicit conversion to `T` — including a nullable-annotated reference property against
a non-nullable reference `T` — fails the build with [ZV0021](diagnostics.md#zv0021); the
diagnostic suggests declaring the rule as `ValidationAttribute<T?>` when that is the mismatch.

**Collections validate the property itself, not each element.** A custom rule on a
`List<string>` property receives the whole list as `T`, the same as a built-in rule would; it
does not iterate elements. Per-element validation is not supported by this mechanism.

### [RuleMessage] and message precedence

`[RuleMessage(message)]`, placed on the attribute class, sets its default failure message and,
optionally, its default `ErrorCode`:

```csharp
[RuleMessage("{PropertyName} must not be blank.", ErrorCode = "NOT_BLANK")]
public sealed class NotBlankAttribute : ValidationAttribute<string?>
{
    public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
}
```

The message is resolved in this order, and the same order applies to `ErrorCode`:

1. `Message` set on the usage, `[NotBlank(Message = "...")]` — today's behavior for every rule.
2. `[RuleMessage]` on the attribute class, or the nearest base class that declares one.
3. The fallback `"{PropertyName} is invalid."`, the same fallback `[Must]` uses.

An `ErrorCode` written on the usage wins even when it is `null`: `[NotBlank(ErrorCode = null)]`
clears the code the rule's `[RuleMessage]` declares, and the failure carries no error code.

`Message`, `When`, `Unless`, `ErrorCode` and `Severity` are recognised as the members declared
on `ValidationAttribute`. If a rule attribute declares its own member of the same name, such as
`public new string? Message { get; set; }`, a value written for it on the usage is copied into
the rule instance like any other named argument, and it does not set the failure message.

`[RuleMessage]` is inherited, so a derived rule attribute that declares no `[RuleMessage]` of
its own picks up its nearest base class's.

`[RuleMessage]` has an effect only on a class deriving, directly or indirectly, from
`ValidationAttribute<T>`, including an abstract base rule. On any other class nothing reads it,
and the generator reports [ZV0026](diagnostics.md#zv0026) at the attribute.

### Placeholders

Every placeholder except `{PropertyValue}` is resolved at compile time into a constant string.
`{PropertyValue}` is formatted at validation time, and only when the rule fails; a passing
validation formats nothing.

- `{PropertyName}` and `{PropertyValue}` mean what they do everywhere else.
- `{name}` is replaced by the constructor argument whose **parameter name** matches, or by the
  named property of that name, as written on the usage. For `[MinWords(3)]`, `{minWords}` binds
  the constructor parameter `minWords`. A named property binds only when the usage writes it,
  so the get-only `MinWords` property above never binds `{MinWords}` — the generator never
  reads a property's runtime value. Use the constructor parameter's name.
- Matching is case-sensitive, and constant formatting is invariant-culture: `bool` renders as
  `True` or `False`, and `null` renders as empty text.
- A base member also binds when it is written on the usage: `{ErrorCode}` in a message resolves
  if the usage sets `ErrorCode`, and likewise for `{When}` — these are ordinary named arguments
  from the placeholder resolver's point of view.
- A placeholder that matches nothing is left in the message literally and reported as
  [ZV0022](diagnostics.md#zv0022).
- A placeholder token that appears *inside a substituted value* is expanded again, not treated
  as literal text — tracked as
  [#204](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/204). Avoid passing a
  constructor or named argument whose value itself contains `{` and `}` until that is fixed.

### Limitations

- **No async rules yet.** `ValidationAttribute<T>.IsValid` is synchronous only; there is no
  async counterpart today. An `AsyncValidationAttribute<T>` is tracked in
  [#202](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/202). `[Must]` and
  `[CustomValidation]` — see below — are synchronous too; neither is an async workaround, but
  either is the mechanism for a model-specific check a reusable `ValidationAttribute<T>` rule
  cannot express.

### When to use [Must] instead

Prefer a custom rule attribute, `ValidationAttribute<T>`, when the check is reusable across
models — the same shape of rule applied to different properties on different types. A
`ValidationAttribute<T>` rule only ever sees the one property's value.

Prefer `[Must]` when the check is specific to one model. It calls an instance method on the
model, so the predicate can also read other properties of the same instance, but it still
reports one failure against the one property it decorates.

Use `[CustomValidation]` when the rule belongs to the model as a whole: it can report any
number of failures, against any properties, from one method.

---

## Level 1: [Must] — Inline property predicate

Place `[Must(nameof(MethodName))]` on a property to call an instance method on the model as a predicate.

**Method signature requirement:** `bool MethodName(T value)` where `T` matches the property type.

The generator produces: `!instance.MethodName(instance.PropertyValue)`

**Default error message:** `{PropertyName} is invalid.`

### Example

```csharp
[Validate]
public class Product
{
    [NotEmpty]
    [Must(nameof(IsValidSku))]
    public string Sku { get; set; } = "";

    private bool IsValidSku(string value) =>
        value.StartsWith("SKU-") && value.Length >= 7;
}
```

The predicate is called on the instance being validated, so it can access any other instance member of the model (other properties, helper methods, etc.).

### Custom error message

Override the default message using the `Message` property inherited from `ValidationAttribute`:

```csharp
[Must(nameof(IsValidSku), Message = "SKU must start with 'SKU-' and be at least 7 characters.")]
public string Sku { get; set; } = "";
```

---

## Level 2: [CustomValidation] — Cross-property instance method

Place `[CustomValidation]` on an instance method of the model class (not a property) to run cross-property validation logic with full access to `this`.

**Method signature requirement:** no parameters, returning one of `IEnumerable<ValidationFailure>`, `ValidationFailure[]`, or `ReadOnlySpan<ValidationFailure>`.

The generator produces:

```csharp
foreach (var _cf in instance.MethodName()) _buf.Add(_cf);
```

### Which return type to use

The three are interchangeable in behaviour, but not in cost. A method written with `yield return` is an iterator, and calling an iterator allocates its state machine — **before it has yielded anything**. So a model that passes validation still pays for it on every call:

| return type | allocation when the model is valid |
|---|---:|
| `IEnumerable<ValidationFailure>` via `yield` | 56 B |
| `ValidationFailure[]` | **0 B** |
| `ReadOnlySpan<ValidationFailure>` | **0 B** |

`yield return` reads well and is fine when validation failures are the common case or the path is cold. For anything hot, return an array and hand back a cached empty one when there is nothing to report:

```csharp
[CustomValidation]
public ValidationFailure[] ValidateBudget() =>
    Budget >= 0
        ? Array.Empty<ValidationFailure>()
        : [new ValidationFailure { PropertyName = nameof(Budget), ErrorMessage = "Budget must not be negative." }];
```

`ReadOnlySpan<ValidationFailure>` behaves the same way and lets you return a span over pre-built static failures. Remember a span cannot point at a local array, so the failures need to live in a field or a static.

`[CustomValidation]` methods run **after** all property-level rules have been evaluated. When `[Validate].StopOnFirstFailure = true`, custom methods are only reached if all property groups pass.

### Example

```csharp
[Validate]
public class PasswordChange
{
    [NotEmpty]
    public string CurrentPassword { get; set; } = "";

    [NotEmpty]
    [MinLength(8)]
    public string NewPassword { get; set; } = "";

    [NotEmpty]
    public string ConfirmPassword { get; set; } = "";

    [CustomValidation]
    public IEnumerable<ValidationFailure> ValidatePasswordMatch()
    {
        if (NewPassword != ConfirmPassword)
            yield return new ValidationFailure
            {
                PropertyName = nameof(ConfirmPassword),
                ErrorMessage = "Passwords do not match.",
                Severity     = Severity.Error
            };
    }
}
```

Your method constructs and yields `ValidationFailure` values directly, giving you full control over `PropertyName`, `ErrorMessage`, `ErrorCode`, and `Severity`.

### Multiple [CustomValidation] methods

Multiple `[CustomValidation]` methods are allowed on the same class. They run in declaration order, after all property rules.

### Important: [CustomValidation] does not extend ValidationAttribute

`[CustomValidation]` extends `System.Attribute` directly, **not** `ValidationAttribute`. It therefore does **not** support `Message`, `When`, `Unless`, `ErrorCode`, or `Severity` properties on the attribute itself. Your method is responsible for constructing the `ValidationFailure` values it yields.

---

## ZV0013 compiler diagnostic

If a method decorated with `[CustomValidation]` has the wrong signature — has parameters, or returns something other than `IEnumerable<ValidationFailure>`, `ValidationFailure[]` or `ReadOnlySpan<ValidationFailure>` — the generator emits a **ZV0013** compile-time error:

> Method 'MethodName' decorated with [CustomValidation] must have no parameters and return IEnumerable\<ValidationFailure\>, ValidationFailure[] or ReadOnlySpan\<ValidationFailure\>

This is caught at compile time, not at runtime.

---

## Choosing between [Must] and [CustomValidation]

| Use case | Recommendation |
|---|---|
| Single-property predicate (pure validation) | `[Must]` — concise, inline |
| Cross-property rules (e.g., confirm password) | `[CustomValidation]` — full access to model |
| Complex rule needing custom error code/severity | `[CustomValidation]` — construct the `ValidationFailure` directly |
| Simple format check | `[Must]` |
