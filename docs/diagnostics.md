---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZV0011–ZV0024 Roslyn analyzer rules emitted by ZeroAlloc.Validation.Generator, with triggers, severities, and fix guidance.
sidebar_position: 11
---

# Compiler Diagnostics

ZeroAlloc.Validation.Generator emits the following Roslyn diagnostics at compile time.

| ID | Severity | Title |
|---|---|---|
| [ZV0011](#zv0011) | Warning | Redundant [ValidateWith] attribute |
| [ZV0012](#zv0012) | Error | Invalid [ValidateWith] validator type |
| [ZV0013](#zv0013) | Error | Invalid [CustomValidation] method signature |
| [ZV0014](#zv0014) | Warning | [Validate] on non-readonly struct |
| [ZV0015](#zv0015) | Error | Duplicate pipeline behavior Order |
| [ZV0016](#zv0016) | Warning | Multi-property value-object can't be auto-unwrapped |
| [ZV0017](#zv0017) | Warning | Validation rules depending on an inaccessible base member are ignored |
| [ZV0018](#zv0018) | Warning | Duplicate validation attribute |
| [ZV0019](#zv0019) | Error | Invalid ZeroAllocGeneratedAccessibility value |
| [ZV0020](#zv0020) | Error | ValidationAttribute subclass the generator cannot emit |
| [ZV0021](#zv0021) | Error | Custom rule value type does not match the property type |
| [ZV0022](#zv0022) | Warning | Unknown placeholder in a custom rule message |
| [ZV0023](#zv0023) | Error | Custom rule attribute not accessible from the generated validator |
| [ZV0024](#zv0024) | Error | Validation attribute applied where the generator does not read it |

---

## ZV0011

**Severity:** Warning

**Title:** Redundant [ValidateWith] attribute

**When fired:** `[ValidateWith]` is applied to a property whose type already carries `[Validate]`. The auto-generated validator is used by default — `[ValidateWith]` is only needed for types you do not control.

**Fix:** Remove `[ValidateWith]` from the property and rely on the auto-generated validator, or keep it only if you need to override the default with a custom implementation.

---

## ZV0012

**Severity:** Error

**Title:** Invalid [ValidateWith] validator type

**When fired:** The type argument passed to `[ValidateWith(typeof(T))]` does not implement `ValidatorFor<TProperty>` for the property type.

**Fix:** Replace the type argument with a class that extends `ValidatorFor<TProperty>`, where `TProperty` matches the type of the annotated property.

---

## ZV0013

**Severity:** Error

**Title:** Invalid [CustomValidation] method signature

**When fired:** A method decorated with `[CustomValidation]` has parameters, or does not return `IEnumerable<ValidationFailure>`.

**Fix:** Ensure the method has no parameters and returns `IEnumerable<ValidationFailure>`:

```csharp
[CustomValidation]
public IEnumerable<ValidationFailure> ValidateBusinessRules()
{
    // yield return failures as needed
}
```

---

## ZV0014

**Severity:** Warning

**Title:** `[Validate]` on non-readonly struct

**When fired:** You decorated a `struct` or `record struct` with `[Validate]`,
but the type is not declared `readonly`. A caller can mutate the instance
between the validator returning `IsValid == true` and the consumer reading
the value — making the validation result stale.

**Fix:** Declare the type as `readonly struct` or `readonly record struct`:

```csharp
[Validate]
public readonly record struct PlaceOrderCommand(
    [property: GreaterThan(0)] int CustomerId,
    [property: GreaterThan(0)] decimal Total);
```

**Suppressing:** If your call site cooperates with the hazard (e.g. you validate
inside the same method that constructs the struct and never mutate after),
suppress with `#pragma warning disable ZV0014` around the type declaration,
or add `<NoWarn>$(NoWarn);ZV0014</NoWarn>` in the consuming project.

---

## ZV0015

**Severity:** Error

**Title:** Duplicate pipeline behavior Order

**When fired:** Two `[PipelineBehavior]` classes targeting the same model have the same `Order` value. The execution order of the behavior chain would be ambiguous.

**Fix:** Assign a unique `Order` value to each behavior:

```csharp
[PipelineBehavior(Order = 0)]
public class LoggingBehavior : IPipelineBehavior { /* ... */ }

[PipelineBehavior(Order = 1)]   // was also 0 — now unique
public class AuditBehavior : IPipelineBehavior { /* ... */ }
```

---

## ZV0016

**Severity:** Warning

**Title:** Multi-property value-object can't be auto-unwrapped

**When fired:** A property carries a built-in operand validator (e.g. `[GreaterThan]`, `[NotEmpty]`) and its type is decorated with `[ZeroAlloc.ValueObjects.ValueObject]` but exposes more than one public instance property. Auto-unwrap only works for single-property wrappers — there is no single underlying value to compare against.

**Fix:** Either expose the validation through a single-property wrapper, or replace the built-in operand validator with `[Must]` / `[CustomValidation]` carrying a custom predicate that knows how to inspect the multi-property type:

```csharp
[ValueObject]
public readonly partial struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }
    public Money(decimal amount, string currency) { Amount = amount; Currency = currency; }
}

[Validate]
public partial class PriceCommand
{
    // [GreaterThan(0)] Money Total      // would emit ZV0016 — Money has two properties
    [Must(nameof(IsPositive))]
    public Money Total { get; set; }

    public bool IsPositive(Money m) => m.Amount > 0;
}
```

**Suppressing:** If your intent is to constrain a different property (e.g. only the `.Amount` component), refactor the model so the constrained surface is a single-property value-object. To silence the warning without restructuring, add `#pragma warning disable ZV0016` around the property declaration or `<NoWarn>$(NoWarn);ZV0016</NoWarn>` in the consuming project — but note that the underlying validator emission will still be incorrect for the multi-property case; a custom predicate is the recommended fix.

---

## ZV0017

**Severity:** Warning

**Title:** Validation rules depending on an inaccessible base member are ignored

**When fired:** A `[Validate]` type inherits from a base type that declares validation rules, but the member those rules depend on cannot be referenced from the generated validator. The generated validator is a separate class, so it can reach `public` members — and `internal` ones when the base type lives in the same assembly — but never `private` or `protected` ones. Two cases fire this:

- a base property carrying rule attributes is `protected` or `private` (or exposes no accessible getter);
- a rule on an otherwise-reachable property names a `When` / `Unless` method that is `protected` or `private` on a base type.

In both cases the rule is dropped rather than emitted as code that would not compile.

**Fix:** Widen the member to `public` (or `internal` within the same assembly), or move it onto the derived type:

```csharp
public abstract class AuditedBase
{
    [NotEmpty]
    protected string? ModifiedBy { get; init; }   // ZV0017 — rule silently dropped

    [NotEmpty(When = nameof(ShouldCheck))]
    public string? Reference { get; init; }       // ZV0017 — guard is unreachable

    protected bool ShouldCheck() => true;
}
```

```csharp
public abstract class AuditedBase
{
    [NotEmpty]
    public string? ModifiedBy { get; init; }      // reachable

    [NotEmpty(When = nameof(ShouldCheck))]
    public string? Reference { get; init; }

    public bool ShouldCheck() => true;            // reachable
}
```

**Suppressing:** If the member is deliberately hidden and you do not want it validated, set `[Validate(IncludeBaseProperties = false)]` on the derived type to opt out of base-type rules entirely, or add `<NoWarn>$(NoWarn);ZV0017</NoWarn>`. Note that suppressing leaves the rule unenforced.

---

## ZV0018

**Severity:** Warning

**Title:** Duplicate validation attribute

**When fired:** A property declares the same rule attribute more than once with identical arguments. The rule is evaluated twice and reports the same failure twice.

Rule attributes are `AllowMultiple` because repeating a check with *different* arguments is meaningful — two `[Must]` predicates, or a `[Matches]` for each of several patterns. Only an exact repeat is flagged:

```csharp
[Validate]
public class Request
{
    [NotEmpty]
    [NotEmpty]                       // ZV0018 — reports "must not be empty" twice
    public string? Tenant { get; init; }

    [MinLength(3)]
    [MinLength(5)]                   // fine — different bounds
    public string Region { get; init; } = "";

    [Must(nameof(IsShort))]
    [Must(nameof(IsLower))]          // fine — different predicates
    public string Code { get; init; } = "";
}
```

Named arguments are compared order-independently, so `[NotEmpty(ErrorCode = "A", Message = "x")]` and `[NotEmpty(Message = "x", ErrorCode = "A")]` count as duplicates.

**Fix:** Remove the repeated attribute. If both were meant to check different things, give them different arguments.

**Suppressing:** `#pragma warning disable ZV0018` around the property, or `<NoWarn>$(NoWarn);ZV0018</NoWarn>`. Note that the duplicate failure is still reported at runtime.

---

## ZV0019

**Severity:** Error

**Title:** Invalid ZeroAllocGeneratedAccessibility value

**When fired:** The `ZeroAllocGeneratedAccessibility` MSBuild property (see [Generated accessibility](advanced.md#generated-accessibility--keeping-validators-out-of-a-librarys-public-api)) is set to a value other than `Public` or `Internal`. Rather than silently fall back to a default, the generator reports ZV0019 for the whole compilation and treats the value as `Public` — today's behavior — so the rest of the build is not thrown off by a typo in this one property.

The comparison is case-insensitive: `Internal`, `internal` and `INTERNAL` are all valid. Leaving the property unset, or setting it to an empty string, is also valid and means `Public`.

This diagnostic is reported once per compilation, independent of whether the project has any `[Validate]` model the generator would otherwise emit a validator for.

```xml
<PropertyGroup>
  <!-- ZV0019: ZeroAllocGeneratedAccessibility is set to 'Priv4te'. Allowed values are 'Public' and 'Internal'. -->
  <ZeroAllocGeneratedAccessibility>Priv4te</ZeroAllocGeneratedAccessibility>
</PropertyGroup>
```

**Fix:** Set the property to `Public` or `Internal`:

```xml
<PropertyGroup>
  <ZeroAllocGeneratedAccessibility>Internal</ZeroAllocGeneratedAccessibility>
</PropertyGroup>
```

Or remove the property entirely to keep the default (`Public`).

---

## ZV0020

**Severity:** Error

**Title:** ValidationAttribute subclass the generator cannot emit

**When fired:** A property on a `[Validate]` model carries an attribute that derives from `ValidationAttribute`, but the attribute is neither one of the built-in rule attributes nor a subclass of `ValidationAttribute<T>` (see [Custom rule attributes](custom-validation.md)). The generator has no way to evaluate it, so without this error the property would go unvalidated with nothing to say so:

```csharp
public sealed class LegacyRuleAttribute : ValidationAttribute
{
    // does not derive from ValidationAttribute<T>, so it has no IsValid to call
}

[Validate]
public class Order
{
    [LegacyRule]                     // ZV0020
    public string? Reference { get; set; }
}
```

> '{Attr}' derives from ValidationAttribute but the generator cannot emit it; derive from ValidationAttribute\<T\> and override IsValid

**Fix:** Derive the attribute from `ValidationAttribute<T>` and override `IsValid`, or remove the attribute if it was never meant to be a rule:

```csharp
[RuleMessage("{PropertyName} must not be blank.")]
public sealed class NotBlankAttribute : ValidationAttribute<string?>
{
    public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
}
```

This is the breaking change described in [Migrating to v2](migrating-to-v2.md): in 1.x, a `ValidationAttribute` subclass the generator did not recognize was silently ignored, and the property it decorated was never validated.

---

## ZV0021

**Severity:** Error

**Title:** Custom rule value type does not match the property type

**When fired:** A property is decorated with a `ValidationAttribute<T>` rule whose `T` the property's type has no implicit conversion to, checked with `Compilation.ClassifyCommonConversion(...).IsImplicit`. This includes a nullable-annotated reference property checked against a non-nullable reference `T`:

```csharp
public sealed class NotBlankAttribute : ValidationAttribute<string>   // non-nullable T
{
    public override bool IsValid(string value) => !string.IsNullOrWhiteSpace(value);
}

[Validate]
public class Order
{
    [NotBlank]                        // ZV0021 — Reference is string?, the rule's T is string
    public string? Reference { get; set; }
}
```

> '{Attr}' validates '{T}' but property '{Prop}' is '{PropType}'

When the mismatch is exactly a nullable-vs-non-nullable reference type, the message appends a hint naming the fix directly, for example:

> ... Declare the rule as ValidationAttribute\<string?\> to accept null.

**Fix:** Declare the rule against the property's own type, or against a wider type the property converts to implicitly — for a nullable reference property, declare the rule as `ValidationAttribute<T?>`. The rule is left out of the generated validator until the mismatch is fixed; the build already fails on this error, so nothing unvalidated ships.

---

## ZV0022

**Severity:** Warning

**Title:** Unknown placeholder in a custom rule message

**When fired:** A message — the usage's `Message`, or the attribute class's `[RuleMessage]` — contains a `{name}` placeholder that matches neither `PropertyName`, `PropertyValue`, a constructor parameter name, nor a named argument written on the usage:

```csharp
[RuleMessage("{PropertyName} must have at least {MinWords} words.")]   // ZV0022 — {MinWords}
public sealed class MinWordsAttribute(int minWords) : ValidationAttribute<string?>
{
    public int MinWords { get; } = minWords;
    public override bool IsValid(string? value) => true;
}

[Validate]
public class Article
{
    [MinWords(3)]                    // MinWords is not written as a named argument here
    public string? Title { get; set; }
}
```

`{minWords}` — the constructor parameter's own name — would have resolved. `{MinWords}` — the property — resolves only when the usage writes it as a named argument, and a get-only property such as this one cannot be written there: matching is case-sensitive, and the generator never reads a property's runtime value.

The warning is reported at the attribute usage, `[MinWords(3)]`, so a property with several rules points at the one whose message has the unknown placeholder.

> Placeholder '{0}' in the message for '{1}' on '{2}' does not match any argument; it is emitted literally

**Fix:** Match the placeholder's spelling to a constructor parameter name or a named argument actually written on the usage, or remove the placeholder. The message still compiles and runs with the placeholder left in literally, so this is a warning rather than an error.

---

## ZV0023

**Severity:** Error

**Title:** Custom rule attribute not accessible from the generated validator

**When fired:** The generated validator is a separate class in the model's own assembly, declared in its own generated file. A custom rule usage can compile while its attribute type, a type containing it, its constructor, a named member it sets, or a `typeof`/enum argument type is not reachable from that class — for example a `private` or `protected` attribute nested inside the model, which would otherwise surface as CS0122 in generated code. A `file`-local type is never reachable from the generated file, so any of those types declared `file` is reported too:

```csharp
[Validate]
public class Order
{
    private sealed class InternalOnlyAttribute : ValidationAttribute<string?>
    {
        public override bool IsValid(string? value) => true;
    }

    [InternalOnly]                    // ZV0023 — InternalOnlyAttribute is private
    public string? Reference { get; set; }
}
```

> '{0}' cannot be emitted: '{1}' is not accessible from the generated validator; make it internal or public

**Fix:** Make the attribute type — and anything it names, including its constructor, any named member set on the usage, and any `typeof`/enum argument type — `internal` (within the same assembly) or `public`, and not `file`-local. The rule is left out of the generated validator until it is reachable; the build already fails on this error, so nothing unvalidated ships.

---

## ZV0024

**Severity:** Error

**Title:** Validation attribute applied where the generator does not read it

**When fired:** An attribute deriving from `ValidationAttribute` is applied to a field or a constructor parameter of a `[Validate]` model, or of a base type whose properties it validates. The generator reads rules from properties only. `ValidationAttribute<T>` targets properties, but a rule attribute can widen its own `[AttributeUsage]`, and then the usage compiles and the rule never runs:

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotBlankAttribute : ValidationAttribute<string?>
{
    public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
}

[Validate]
public class Order
{
    [NotBlank]                        // ZV0024 — Reference is a field
    public string? Reference;
}

[Validate]
public record Customer([NotBlank] string? Name);   // ZV0024 — applies to the parameter
```

> '{0}' is applied to '{1}', which the generator does not validate; apply it to a property

**Fix:** Apply the rule to a property. Turn the field into a property, and on a record's positional parameter use the `property:` target so the attribute lands on the generated property:

```csharp
[Validate]
public record Customer([property: NotBlank] string? Name);
```

A field or parameter of a base type that is itself `[Validate]` is reported once, by that type.
