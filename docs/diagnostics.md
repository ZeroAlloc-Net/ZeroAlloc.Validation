---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZV0011–ZV0031 Roslyn analyzer rules emitted by ZeroAlloc.Validation.Generator, with triggers, severities, and fix guidance.
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
| [ZV0025](#zv0025) | Error | [Validate] type not accessible from the generated validator |
| [ZV0026](#zv0026) | Warning | [RuleMessage] on a class that is not a custom rule |
| [ZV0027](#zv0027) | Error | Validation attribute applied to a property the generated validator cannot read |
| [ZV0028](#zv0028) | Error | Validation method the generated validator cannot call |
| [ZV0029](#zv0029) | Error | [Validate] on a generic type |
| [ZV0030](#zv0030) | Error | Validation method call that does not compile |
| [ZV0031](#zv0031) | Error | Two [Validate] models whose validators would have the same name |

---

## ZV0011

**Severity:** Warning

**Title:** Redundant [ValidateWith] attribute

**When fired:** `[ValidateWith]` is applied to a property whose type already carries `[Validate]`. The auto-generated validator is used by default — `[ValidateWith]` is only needed for types you do not control. A `[Validate]` type that gets no generated validator, [ZV0025](#zv0025) or [ZV0029](#zv0029), is not reported: `[ValidateWith]` is then the way to validate a property of that type.

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

**When fired:** A method decorated with `[CustomValidation]` is generic, has parameters, or does not return `IEnumerable<ValidationFailure>`, `ValidationFailure[]` or `ReadOnlySpan<ValidationFailure>`. A generic method is an error because `instance.Check()` gives the compiler nothing to infer its type arguments from.

The signature is checked first. A method that is also static, or also inaccessible on the `[Validate]` type itself, reports ZV0013 only; once the signature is fixed, [ZV0028](#zv0028) reports the rest. An inaccessible instance method on a base type reports [ZV0017](#zv0017) only, whatever its signature. A method declared on a base type that is itself `[Validate]` is reported once, by that type. If that base type sets `IncludeBaseProperties = false`, it does not see the types above it, so the derived type reports their methods. A method on a base type from a referenced assembly is never reported, as for [ZV0027](#zv0027), and is left out of the validator. A `[CustomValidation]` method is the attributed method itself, so it is never reported as [ZV0030](#zv0030): if the model declares another member of the same name that `instance.Check()` would bind to, the validator calls the method through the type that declares it instead.

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

**When fired:** A `[Validate]` type inherits from a base type that declares validation rules, but the member those rules depend on cannot be referenced from the generated validator. The generated validator is a separate class, so it can reach `public` members — and `internal` ones when the base type lives in the same assembly, or in one that grants yours `[InternalsVisibleTo]` — but never `private` or `protected` ones. Three cases fire this:

- a base property carrying rule attributes is `protected` or `private`, or its getter is;
- a `[CustomValidation]` method on a base type is `protected` or `private`;
- a rule on an otherwise-reachable property names a `When` / `Unless` method or a `[Must]` predicate that is `protected` or `private` on a base type.

In each case the rule is dropped rather than emitted as code that would not compile. A base property that is static, an indexer or has no getter is reported as [ZV0027](#zv0027) instead, because widening it would not make it readable. A static method is reported as [ZV0028](#zv0028) instead, wherever it is declared, because widening it would not make it callable. So is an inaccessible method declared on the `[Validate]` type itself, since that type is yours to change. A rule declared on a base type that is itself `[Validate]` is reported once, by that type. A member of a base type from a referenced assembly has no source location, so its warning is reported at the derived type's `[Validate]` attribute.

**Fix:** Widen the member to `public`, or to `internal` within the same assembly or one that grants yours `[InternalsVisibleTo]`, or move it onto the derived type:

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

**Not reported:** A rule on an `internal` member of a base type in another assembly that does not grant yours `[InternalsVisibleTo]` is neither validated nor reported. The compiler does not load such members from a referenced assembly, so the generator cannot see them. Grant `[InternalsVisibleTo]` to your assembly, or move the rule to a member the validator can reach.

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

**When fired:** The generated validator is a separate class in the model's own assembly, declared in its own generated file. A custom rule usage can compile while its attribute type, a type containing it, a type argument of a generic rule such as `[Rule<Secret>]`, its constructor, a named member it sets, or a `typeof`/enum argument type is not reachable from that class — for example a `private` or `protected` attribute nested inside the model, which would otherwise surface as CS0122 in generated code. A `file`-local type is never reachable from the generated file, so any of those types declared `file` is reported too:

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

**Fix:** Make the attribute type — and anything it names, including its type arguments, its constructor, any named member set on the usage, and any `typeof`/enum argument type — `internal` (within the same assembly) or `public`, and not `file`-local. The rule is left out of the generated validator until it is reachable; the build already fails on this error, so nothing unvalidated ships.

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

A rule written with the `field:` target on an auto-property, such as `[field: NotBlank] public string? Name { get; init; }`, lands on the compiler's backing field and is reported with the property's name; drop the `field:` target to apply it to the property.

A field or parameter of a base type that is itself `[Validate]` is reported once, by that type. If that base type sets `IncludeBaseProperties = false`, it does not see the types above it, so the derived type, whose validator still inherits their rules, reports those. A base type from a referenced assembly is never reported, as for [ZV0027](#zv0027).

---

## ZV0025

**Severity:** Error

**Title:** [Validate] type not accessible from the generated validator

**When fired:** The generated `{Model}Validator` is a top-level class in the model's namespace, declared in its own generated file. It can validate a model nested in another type, as long as the model and every type containing it are `public`, `internal` or `protected internal`. ZV0025 is reported at the `[Validate]` attribute of a model it cannot reach:

- a `private`, `protected` or `private protected` nested type;
- a type of any accessibility nested inside such a type;
- a `file`-local type, or a type nested inside one.

```csharp
public class Checkout
{
    [Validate]                        // ZV0025 — Address is private
    private class Address
    {
        [NotEmpty] public string City { get; set; } = "";
    }

    private class Steps
    {
        [Validate]                    // ZV0025 — Payment is inside a private type
        public class Payment { }
    }
}

[Validate]                            // ZV0025 — file-local
file class Draft { }
```

> The generated validator cannot access '{0}', so no validator is generated for it; make it and every type containing it internal or public

No validator is generated for the type. `AddZeroAllocValidators()`, `ValidateWithZeroAlloc()` and the ASP.NET Core filter leave it out, and a property of that type is not validated as a nested model. The other diagnostics are not reported for the type's members: they describe how the generated validator treats its rules, and there is none. They run as usual once the type is reachable.

**Fix:** Make the type and every type containing it `internal` or `public`, and drop the `file` modifier. `internal` keeps the type out of the public API:

```csharp
public class Checkout
{
    [Validate]
    internal class Address
    {
        [NotEmpty] public string City { get; set; } = "";
    }
}
// Generated: Checkout_AddressValidator
```

---

## ZV0026

**Severity:** Warning

**Title:** [RuleMessage] on a class that is not a custom rule

**When fired:** `[RuleMessage]` is applied to a class that does not derive, directly or through a
base class, from `ValidationAttribute<T>`. The generator reads `[RuleMessage]` only for custom
rules, so on any other class, including a subclass of the non-generic `ValidationAttribute`, the
message is never used:

```csharp
[RuleMessage("{PropertyName} must not be blank.")]   // ZV0026 — not a ValidationAttribute<T>
public sealed class NotBlankAttribute : Attribute { }
```

An abstract base rule, such as `abstract class StringRule : ValidationAttribute<string?>`, is a
custom rule, so a `[RuleMessage]` on it is not reported; derived rules inherit it.

The warning is reported at the `[RuleMessage]` attribute, whether or not the project has any
`[Validate]` model.

> '{0}' has [RuleMessage] but does not derive from ValidationAttribute<T>, so the message is never used

**Fix:** Derive the class from `ValidationAttribute<T>` if it is meant to be a rule, or remove the
`[RuleMessage]`. Nothing is generated differently, so this is a warning rather than an error.

---

## ZV0027

**Severity:** Error

**Title:** Validation attribute applied to a property the generated validator cannot read

**When fired:** A rule, meaning any attribute deriving from `ValidationAttribute` such as a built-in rule, `[Must]` or a custom `ValidationAttribute<T>`, or a `[ValidateWith]` is applied to a property that the generated validator cannot read. The validator reads each property as `instance.Property`, which does not compile when the property:

- is `static`;
- is an indexer;
- has no `get` accessor;
- is `private` or `protected`, or its `get` accessor is, when the property is declared on the `[Validate]` type itself.

```csharp
[Validate]
public class Order
{
    [NotEmpty]                               // ZV0027 — static
    public static string? DefaultCurrency { get; set; }

    [MaxLength(64)]                          // ZV0027 — no get accessor
    public string? Password { set => _hash = Hash(value); }

    [NotBlank]                               // ZV0027 — the getter is private
    public string? Reference { private get; set; }

    [NotEmpty]                               // ZV0027 — an indexer
    public string this[int index] => _lines[index];
}
```

> '{0}' is applied to '{1}', which the generated validator cannot read because the property {2}

The rule is left out rather than emitted as code that fails with CS0176, CS0154, CS0271 or CS0122, and every other property is still validated. Each attribute is reported, so a property with two rules reports twice.

On a base type declared in source, a static, indexer or getter-less property is reported the same way, unless the derived type hides it with a readable property of the same name. A base property that is only inaccessible stays [ZV0017](#zv0017), a warning. A base type that is itself `[Validate]` reports its own members, as ZV0027 on its own properties, so a derived type does not report them again. If that base type sets `IncludeBaseProperties = false`, it does not see the types above it, so the derived type, whose validator still inherits their rules, reports those. A base type from a referenced assembly is never reported: its unreadable rules are left out. If a base type you cannot change carries such a rule, set `[Validate(IncludeBaseProperties = false)]` on the derived type to stop inheriting base-type rules. A property of a `[Validate]` type is composed into the parent's validator without any attribute, so when such a property cannot be read it is simply not composed, and nothing is reported.

**Fix:** Put the rule on a public or internal instance property with a readable getter, or remove it. An override that declares only a setter is readable, because `instance.Property` calls the inherited getter. To validate state kept in a static or write-only member, expose it through a readable property, or check it in a `[CustomValidation]` method.

---

## ZV0028

**Severity:** Error

**Title:** Validation method the generated validator cannot call

**When fired:** The generated validator is a separate class, so it calls the model's methods as `instance.Method(...)`. That call does not compile when the method a rule depends on is static, or is `private`, `protected` or `private protected`. Five usages are checked:

- a `[CustomValidation]` method;
- the predicate a `[Must]` names;
- the method a rule's `When` names;
- the method a rule's `Unless` names;
- the method a model's `[SkipWhen]` names.

```csharp
[Validate]
public class Order
{
    [Must(nameof(IsKnownCode))]                  // ZV0028 — the method is private
    public string? Code { get; set; }

    [NotEmpty(When = nameof(IsShipped))]         // ZV0028 — the method is static
    public string? TrackingNumber { get; set; }

    [CustomValidation]                           // ZV0028 — the method is private
    private IEnumerable<ValidationFailure> CheckTotals() { yield break; }

    private bool IsKnownCode(string? code) => code is "A" or "B";
    public static bool IsShipped() => true;
}
```

> Method '{0}', used by {1}, cannot be called from the generated validator because it {2}

The error is reported at the attribute, or at the model's `[Validate]` attribute for a rule on a property of a base type from a referenced assembly, which has no source location; the message names the method and the property the rule is on. That rule is left out rather than emitted as code that fails with CS0122 or CS0176, and every other rule on the model is still validated. For `[SkipWhen]` the skip check is left out, so the model is always validated. For a `[Must]`, `When`, `Unless` or `[SkipWhen]` method, the generator compiles the call the validator would contain and reports ZV0028 when the compiler rejects it with CS0176, because it binds to a static method, or CS0122, because it binds to one the validator cannot access. Any other error is [ZV0030](#zv0030).

The generated validator lives in the model's assembly, so `internal` and `protected internal` methods are callable and are not reported.

On a base type, a static method is reported the same way. A base method that is only inaccessible stays [ZV0017](#zv0017), a warning, because the base type may not be yours to change. The exception is `[SkipWhen]`: it is read from the model only, so the usage is always yours to change, and an inaccessible base method it names is ZV0028. A `[CustomValidation]` method with an invalid signature that is static, or inaccessible on the `[Validate]` type itself, is reported as [ZV0013](#zv0013) only; an inaccessible instance method on a base type is reported as ZV0017 only. A `[CustomValidation]` method on a base type from a referenced assembly is never reported, as for ZV0013.

**Fix:** Make the method a `public` or `internal` instance method.

---

## ZV0029

**Severity:** Error

**Title:** [Validate] on a generic type

**When fired:** The generated `{Model}Validator` is a non-generic class, so it has no type parameters to name a generic model with. Support for generic models is tracked in [#238](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/238). ZV0029 is reported at the `[Validate]` attribute of a model that is generic, or that is declared inside a generic type:

```csharp
[Validate]                            // ZV0029 — Page<T> is generic
public class Page<T>
{
    [NotEmpty] public string Title { get; set; } = "";
}

public class Envelope<T>
{
    [Validate]                        // ZV0029 — Header is inside Envelope<T>
    public class Header
    {
        [NotEmpty] public string Id { get; set; } = "";
    }
}
```

> '{0}' is generic or declared inside a generic type, so no validator is generated for it; validate a non-generic type instead

No validator is generated for the type, and a closed form such as `Page<Order>` has none either. `AddZeroAllocValidators()`, `ValidateWithZeroAlloc()` and the ASP.NET Core filter leave it out, and a property of that type is not validated as a nested model unless it names a hand-written validator with `[ValidateWith]`. The other diagnostics are not reported for the type's members, since there is no validator for them to describe. A non-generic `[Validate]` model that derives from the generic type, such as `class OrderPage : Page<Order>`, still gets a validator: it validates the inherited properties and reports their diagnostics itself.

A type the generated validator also cannot reach reports [ZV0025](#zv0025) as well, since it needs both changes.

**Fix:** Put `[Validate]` on a non-generic type, and move a model out of a generic containing type. To share rules across models, declare them on a generic base type and put `[Validate]` on each non-generic model that derives from it:

```csharp
public class Page<T>
{
    [NotEmpty] public string Title { get; set; } = "";
}

[Validate]
public class OrderPage : Page<Order> { }
// Generated: OrderPageValidator, which validates Title
```

---

## ZV0030

**Severity:** Error

**Title:** Validation method call that does not compile

**When fired:** The generated validator calls the method a `[Must]` names as `instance.Method(instance.Property)`, and the method a `When`, `Unless` or `[SkipWhen]` names as `instance.Method()`, using the result as a condition. Before emitting a call, the generator compiles it in the generated file's own context: its `using` directives, the model's namespace and a class outside the model. ZV0030 is reported when the compiler rejects the call for any reason other than a static or inaccessible method, which is [ZV0028](#zv0028), or a member that does not exist at all, described below. Typical causes:

- the name is empty, `null` or not an identifier. A method declared with a keyword name, such as `@class`, is not reported: `nameof(@class)` gives `class`, and the call is written `instance.@class()`;
- no overload takes the arguments (CS1501, CS7036, CS1503);
- the call is ambiguous (CS0121), or a generic method's type arguments cannot be inferred (CS0411) or violate its constraints (CS0453 and similar);
- the result cannot be used as a condition (CS0019, CS0023, CS0029, CS0266).

```csharp
[Validate]
[SkipWhen(nameof(IsDraft))]                      // ZV0030 — CS0266, IsDraft returns bool?
public class Order
{
    [Must(nameof(IsKnownCode))]                  // ZV0030 — CS1503, takes an int, Code is a string
    public string? Code { get; set; }

    [NotEmpty(When = nameof(IsShipped))]         // ZV0030 — CS7036, IsShipped needs an argument
    public string? TrackingNumber { get; set; }

    public bool? IsDraft() => null;
    public bool IsKnownCode(int code) => code > 0;
    public bool IsShipped(int carrier) => carrier > 0;
}
```

> Method '{0}', used by {1}, cannot be called by the generated validator: {2}

The last part quotes the call and the compiler's error for it, for example `'instance.IsKnownCode(instance.Code)' fails with CS1503: Argument 1: cannot convert from 'string' to 'int'`.

The error is reported at the attribute. That rule is left out rather than emitted as code that does not compile, and every other rule on the model is still validated. For `[SkipWhen]` the skip check is left out, so the model is always validated. A rule on a property of a base type from a referenced assembly has no source location, so it is reported at the model's `[Validate]` attribute; the message names the method and the property the rule is on.

**A member that does not exist is not reported.** The check compiles against the generator's input, which does not contain what other source generators add to the compilation. When the call fails only because no member of that name exists (CS1061, CS0117, CS0103, or CS1929 for an extension method of that name that takes another receiver), another generator may add the method or an extension method. So the call is emitted as it was before 2.0, and the final compilation, which contains every generator's output, decides. If nothing adds the member, that final compilation fails with the compiler's own error in the generated file.

Because the compiler decides, anything it binds keeps working: an overload chosen by C# overload resolution, a generic method whose type arguments are inferred, an extension method in scope of the generated file, a delegate-typed field or property, and a method whose result converts to `bool`. Warnings do not count. The generated file imports only `ZeroAlloc.Validation` and your global usings, and sits in the model's namespace. An extension method imported only by a `using` in the model's own file is therefore not in scope there. The call fails with CS1061, which is left to the final compilation as described above.

A `[CustomValidation]` method's signature is [ZV0013](#zv0013)'s to check, so it never reports ZV0030.

**Fix:** Name a method the call can bind to: `public` or `internal`, taking no arguments for `When`, `Unless` and `[SkipWhen]` or the property's value for `[Must]`, and returning `bool`. The compiler's error in the message says what is wrong.

---

## ZV0031

**Severity:** Error

**Title:** Two [Validate] models whose validators would have the same name

**When fired:** The validator for a model nested in other types is named after the containing types and the model, joined with underscores: `Outer.Request` gets `Outer_RequestValidator`. A type whose own name contains an underscore can therefore claim the same name in the same namespace:

```csharp
public class Outer
{
    [Validate]                        // ZV0031 — Outer_RequestValidator, like Outer_Request
    public class Request { [NotEmpty] public string Name { get; set; } = ""; }
}

[Validate]                            // ZV0031 — Outer_RequestValidator, like Outer.Request
public class Outer_Request { [NotEmpty] public string Name { get; set; } = ""; }
```

`A.B_Request` and `A_B.Request` collide the same way. ZV0031 is reported at the `[Validate]` attribute of each model involved, and names the others:

> The validator for '{0}' would be named '{1}', the same as the validator for {2}, so no validator is generated for these types; rename one of them

No validator is generated for any of them, so the build does not fail with a duplicate type or hint name inside generated code. `AddZeroAllocValidators()`, `ValidateWithZeroAlloc()` and the ASP.NET Core filter leave them out, and a property of one of those types is not validated as a nested model. A type that gets no validator anyway, because it is not `[Validate]`, is generic ([ZV0029](#zv0029)) or is out of the validator's reach ([ZV0025](#zv0025)), claims no name and does not collide. Neither does a type in another namespace.

The generator does not pick another name for one of them: that would rename a validator existing code already refers to.

**Fix:** Rename one of the types. The .NET naming guidelines rule out underscores in type names, so the type with the underscore is usually the one to rename.
