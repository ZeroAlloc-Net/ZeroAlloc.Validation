---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZV0011–ZV0038 Roslyn analyzer rules emitted by ZeroAlloc.Validation.Generator, with triggers, severities, and fix guidance.
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
| [ZV0029](#zv0029) | Error | [Validate] on a generic type whose type parameters the validator cannot declare |
| [ZV0030](#zv0030) | Error | Validation method call that does not compile |
| [ZV0031](#zv0031) | Error | Two [Validate] models whose validators would have the same name |
| [ZV0032](#zv0032) | Warning | Validation call that raises a compiler warning in the generated validator |
| [ZV0033](#zv0033) | Error | Numeric comparison rule on a type that is not a number |
| [ZV0034](#zv0034) | Error | Options validation of a model with asynchronous rules |
| [ZV0035](#zv0035) | Warning | [PipelineBehavior] type that does not implement IPipelineBehavior |
| [ZV0036](#zv0036) | Error | Built-in rule on a value whose type is a type parameter |
| [ZV0037](#zv0037) | Error | Nested model that nests its model inside itself without end |
| [ZV0038](#zv0038) | Warning | Pipeline behavior applied to a closed form of a generic model |

A rule or attribute declared on a base type is checked by every `[Validate]` model that inherits it, and a diagnostic about the usage is reported once, however many models derive from the base type, and whether or not it is `[Validate]` itself. A `[Validate(IncludeBaseProperties = false)]` model does not see the types above it, so it does not report their usages; a model deriving from it that includes base properties does. A generic base type is checked for each type argument the models use, and a diagnostic that is the same for each is reported once.

Most diagnostics depend on the usage alone. The ones about a method a rule calls, or about the call the validator makes, depend on the model too: [ZV0013](#zv0013), [ZV0017](#zv0017), [ZV0028](#zv0028), [ZV0030](#zv0030) and [ZV0032](#zv0032). A model can declare a member that changes what the call binds to. When the base type is `[Validate]`, it reports the usage, and a derived model reports it only when the call fails there and not from the base type. Otherwise each model that finds the same diagnostic at the same place reports it once between them. ZV0017 names the model whose validator leaves the rule out, so it is reported for each such model.

---

## ZV0011

**Severity:** Warning

**Title:** Redundant [ValidateWith] attribute

**When fired:** `[ValidateWith]` is applied to a property whose type already carries `[Validate]`. The auto-generated validator is used by default — `[ValidateWith]` is only needed for types you do not control. A `[Validate]` type that gets no generated validator, [ZV0025](#zv0025) or [ZV0029](#zv0029), is not reported: `[ValidateWith]` is then the way to validate a property of that type. A closing of a generic model, such as a `Page<Order>` property, is reported like any other: `PageValidator<Order>` is its generated validator.

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

**When fired:** Two `[PipelineBehavior]` classes targeting the same model have the same `Order` value. The execution order of the behavior chain would be ambiguous. The diagnostic is reported at the `[PipelineBehavior]` attribute of the second (colliding) behavior and names the first one that already uses the value. When the second behavior's attribute has no source location in the current compilation, it falls back to the model's own `[Validate]` attribute.

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

**When fired:** A property on a `[Validate]` model carries an attribute that derives from `ValidationAttribute`, but the attribute is neither one of the built-in rule attributes nor a subclass of `ValidationAttribute<T>` or `AsyncValidationAttribute<T>` (see [Custom rule attributes](custom-validation.md)). The generator has no way to evaluate it, so without this error the property would go unvalidated with nothing to say so:

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

> '{Attr}' derives from ValidationAttribute but the generator cannot emit it; derive from ValidationAttribute\<T\> and override IsValid, or from AsyncValidationAttribute\<T\> and override IsValidAsync

**Fix:** Derive the attribute from `ValidationAttribute<T>` and override `IsValid`, or from `AsyncValidationAttribute<T>` and override `IsValidAsync` for a check that has to await, or remove the attribute if it was never meant to be a rule:

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

The warning is reported at the attribute usage, `[MinWords(3)]`, so a property with several rules points at the one whose message has the unknown placeholder. It is reported even when the rule is left out because its `When`, `Unless` or `[Must]` method cannot be called, [ZV0017](#zv0017), [ZV0028](#zv0028) or [ZV0030](#zv0030), so fixing the method does not reveal a second warning. [ZV0016](#zv0016) is reported the same way.

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
base class, from `ValidationAttribute<T>` or `AsyncValidationAttribute<T>`. The generator reads `[RuleMessage]` only for custom
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

> '{0}' has [RuleMessage] but does not derive from ValidationAttribute<T> or AsyncValidationAttribute<T>, so the message is never used

**Fix:** Derive the class from `ValidationAttribute<T>` or `AsyncValidationAttribute<T>` if it is meant to be a rule, or remove the
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

**Known limitation: still reported on a `partial` model.** Unlike [ZV0030](#zv0030)'s argument-error case, ZV0028 is reported even when the model, or the base type declaring the method, is `partial`, so another generator adding the instance overload the call would then bind to does not stop it:

```csharp
[Validate]
public partial class Order
{
    [Must(nameof(Ok))] public string? Code { get; set; }   // ZV0028, even though Order is partial
    public static bool Ok(string? v) => true;
}
// another generator: public partial class Order { public bool Ok(object? value) => false; }
```

Leaving this to the final compilation too would fix the rare case where a second generator supplies the overload, but it would also take ZV0028 away from the common mistake of a `private` or `static` rule method on a `partial` model, which would then fail with CS0122 or CS0176 inside the generated file instead of a clear diagnostic. That trade is not worth it; see [#262](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/262). Declare the overload yourself directly on the model so ZV0028 sees a method it can call, or validate the property with a hand-written validator via `[ValidateWith]` instead.

**Fix:** Make the method a `public` or `internal` instance method.

---

## ZV0029

**Severity:** Error

**Title:** [Validate] on a generic type whose type parameters the validator cannot declare

**When fired:** The validator of a generic model, or of a model declared inside a generic type, is generic over the type parameters of the model and of every type containing it, outermost first, with their declared names: `Page<TItem>` gets `PageValidator<TItem>`, and `Envelope<T>.Header` gets `Envelope_HeaderValidator<T>`. See [Generic models](getting-started.md#generic-models). One type parameter list cannot declare the same name twice, so ZV0029 is reported at the `[Validate]` attribute of a model whose type parameters repeat a name along the containing chain, which the compiler already warns about with CS0693, or one named like the validator itself:

```csharp
public class Envelope<T>
{
    [Validate]                        // ZV0029 — T is declared by Envelope<T> and by Part<T>
    public class Part<T>
    {
        [NotEmpty] public string Id { get; set; } = "";
    }
}

[Validate]                            // ZV0029 — the validator is BoxValidator
public class Box<BoxValidator> { }
```

> '{0}' declares type parameter '{1}' more than once along its containing types, so no validator is generated; rename one of them

No validator is generated for the type, and a closed form such as `Envelope<int>.Part<string>` has none either. `AddZeroAllocValidators()`, `ValidateWithZeroAlloc()` and the ASP.NET Core filter leave it out, and a property of that type is not validated as a nested model unless it names a hand-written validator with `[ValidateWith]`. The other diagnostics are not reported for the type's members, since there is no validator for them to describe. A non-generic `[Validate]` model that derives from the type still gets a validator: it validates the inherited properties and reports their diagnostics itself.

Until generic models were supported, [#238](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/238), ZV0029 was reported for every generic model and every model declared inside a generic type. Those now get a validator.

A type the generated validator also cannot reach reports [ZV0025](#zv0025) as well, since it needs both changes.

**Fix:** Rename the type parameter:

```csharp
public class Envelope<T>
{
    [Validate]
    public class Part<TPart> { [NotEmpty] public string Id { get; set; } = ""; }
}
// Generated: Envelope_PartValidator<T, TPart>
```

---

## ZV0030

**Severity:** Error

**Title:** Validation method call that does not compile

**When fired:** The generated validator calls the method a `[Must]` names as `instance.Method(instance.Property)`, and the method a `When`, `Unless` or `[SkipWhen]` names as `instance.Method()`, using the result as a condition. Before emitting a call, the generator compiles it in the generated file's own context: its `using` directives, the model's namespace and a class outside the model. ZV0030 is reported when the compiler rejects the call for any reason other than a static or inaccessible method, which is [ZV0028](#zv0028), a member that does not exist at all, or arguments no method takes on a `partial` model, both described below. Typical causes:

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

**An argument error on a `partial` model is not reported either.** When the model declares a method of that name that does not take the arguments, another generator can still add the overload that does to a `partial` model, and the call then compiles. So when no method of that name takes the arguments (CS1501, CS7036, CS1503, CS0411, CS0453 and similar) or the call is ambiguous between them (CS0121), and the model, or a base type in the same project, is `partial` in every declaration, as are the types containing it, the call is emitted and the final compilation decides, as for a member that does not exist. An ambiguity between the model's own methods is still reported when only a base type is `partial`, since an overload there would not be chosen over them. A call that binds but whose result is not a condition is still reported, on any model. If nothing adds the overload, the final compilation fails with the compiler's own error in the generated file.

Because the compiler decides, anything it binds keeps working: an overload chosen by C# overload resolution, a generic method whose type arguments are inferred, an extension method in scope of the generated file, a delegate-typed field or property, and a method whose result converts to `bool`. A call that compiles with a warning is not ZV0030: it is emitted, and the warning is reported as [ZV0032](#zv0032). The generated file imports only `ZeroAlloc.Validation` and your global usings, and sits in the model's namespace. An extension method imported only by a `using` in the model's own file is therefore not in scope there. The call fails with CS1061, which is left to the final compilation as described above.

**Known limitation: still reported beside a non-partial model's extension method.** The `partial`-model relaxation above only covers another generator adding an instance overload, so it does not apply here even though the outcome is the same. An extension method compiles for any model, `partial` or not, once no instance method is applicable:

```csharp
[Validate]
public class Order
{
    [Must(nameof(Ok))] public string? Code { get; set; }   // ZV0030, even though an extension method binds
    public bool Ok() => true;
}
// another generator: public static class OrderRules { public static bool Ok(this Order o, string? v) => false; }
```

Covering this case would mean never reporting an argument error on any model, `partial` or not, which would remove most of what ZV0030 reports; see [#262](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/262). Declare the overload yourself directly on the model so the generator's own probe can see it, or validate the property with a hand-written validator via `[ValidateWith]` instead.

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

No validator is generated for any of them, so the build does not fail with a duplicate type or hint name inside generated code. `AddZeroAllocValidators()`, `ValidateWithZeroAlloc()` and the ASP.NET Core filter leave them out, and a property of one of those types is not validated as a nested model. A type that gets no validator anyway, because it is not `[Validate]`, has type parameters its validator cannot declare ([ZV0029](#zv0029)) or is out of the validator's reach ([ZV0025](#zv0025)), claims no name and does not collide. Neither does a type in another namespace.

A generic validator's arity is part of its name, so `Box` and `Box<T>` get `BoxValidator` and `BoxValidator<T>` and do not collide. `Outer<T>.Inner` and a top-level `Outer_Inner<T>` both get `Outer_InnerValidator<T>` and do.

The generator does not pick another name for one of them: that would rename a validator existing code already refers to.

**Fix:** Rename one of the types. The .NET naming guidelines rule out underscores in type names, so the type with the underscore is usually the one to rename.

---

## ZV0032

**Severity:** Warning

**Title:** Validation call that raises a compiler warning in the generated validator

**When fired:** The generated validator makes calls for your rules: a `[Must]` predicate as `instance.Method(instance.Property)`, a `When`, `Unless` or `[SkipWhen]` method as `instance.Method()`, a `[CustomValidation]` method, and a custom rule as `IsValid(instance.Property)` on its rule instance. A built-in rule such as `[NotNull]` or `[NotEmpty]` makes no call of its own, but its condition still reads the property directly, e.g. `instance.Code is null`, and that read is covered the same way. A call, or a built-in rule's read, can compile and still make the compiler warn. Common cases are CS8604, when the argument may be null and the parameter does not accept null, and CS0612 or CS0618, when the method or property is `[Obsolete]`. In the generated file, that warning fails a `TreatWarningsAsErrors` build, and you can neither edit nor suppress it there. So the warning is reported as ZV0032 at the rule's attribute instead:

```csharp
[Validate]
public class Order
{
    [Must(nameof(IsKnownCode))]                  // ZV0032 — CS8604, Code may be null
    public string? Code { get; set; }

    [NotNull]
    [Must(nameof(IsKnownRegion))]                // ZV0032 — CS8604, see below
    public string Region { get; set; } = "";

    [NotEmpty(When = nameof(IsShipped))]         // ZV0032 — CS0612, IsShipped is obsolete
    public string? TrackingNumber { get; set; }

    [NotEmpty]                                   // ZV0032 — CS0612, the read of Notes is obsolete
    [Obsolete]
    public string? Notes { get; set; }

    public bool IsKnownCode(string code) => code.Length == 3;
    public bool IsKnownRegion(string region) => region.Length == 2;
    [Obsolete] public bool IsShipped() => true;
}
```

> The generated validator's call '{0}', made for {1}, raises {2}: {3}

The message quotes the call, the usage it is made for, and the compiler's warning ID and text, for example `The generated validator's call 'instance.IsKnownCode(instance.Code)', made for [Must] on 'Code', raises CS8604: Possible null reference argument for parameter 'code' in 'bool Order.IsKnownCode(string code)'.`

**How warnings are found.** The compiler decides, as for [ZV0030](#zv0030). Whether a call warns can depend on the code before it in the generated `Validate` method, not only on the call itself:

- a `[NotNull]`, `[NotEmpty]`, `[MinLength]` or similar rule tests the property for null. The compiler then treats the property as possibly null for the rules after it, even when it is declared non-nullable. At run time, a `Region` that is null does reach `IsKnownRegion` after `[NotNull]` fails. That is why `Region` above is reported.
- under `[StopOnFirstFailure]`, a later rule runs only when the earlier ones pass, so after `[NotNull]` the property is known not to be null, and nothing is reported.
- a `When` method with `[MemberNotNullWhen(true, nameof(Code))]` proves the property is not null for the call it guards, and nothing is reported.

So the generator compiles the validator's own `Validate` body, emitted exactly as the generated file contains it, and reports the warnings the compiler gives inside each of these calls. A warning elsewhere in the generated file is not about one of your calls and is not reported here. A call whose shape leaves no room for a warning is not compiled: a plain predicate or custom rule whose parameter takes the property's type exactly, nullability included, with no attribute on the parameter, no `[Obsolete]`, `[Experimental]` or `System.Diagnostics.CodeAnalysis` attribute on the property or any property it overrides, and no earlier null test of that property; and a guard, `[SkipWhen]` or `[CustomValidation]` method without attributes.

**Severity.** The warning takes the severity your project gives the compiler's ID, through `<NoWarn>`, `<WarningsAsErrors>`, `<TreatWarningsAsErrors>`, a `dotnet_diagnostic.<id>.severity` entry in `.editorconfig`, or a global analyzer config. A warning turned off, or set below a warning, is not reported, and its call gets no pragma. The generated file sits in your project beside the model, so the `.editorconfig` entries that apply to the file declaring the attribute are the ones used. A warning raised to an error, and a diagnostic that is an error by default, such as the one an `[Experimental]` API raises until you opt in, is reported as ZV0032 with severity Error, so the build still fails as it would have in the generated file. Suppressing that ZV0032 is how you opt in for that call.

**In the generated file,** the call is still emitted, since it compiles, and the line holding it is wrapped in a pragma for exactly the warnings it raised:

```csharp
#pragma warning disable CS8604 // mirrored as ZV0032 at the attribute this call is made for
        if (!instance.IsKnownCode(instance.Code))
#pragma warning restore CS8604
```

That is the only pragma of this kind the generator writes, and a call that does not warn gets none. The `CS0612`/`CS0618` pragma around the field of an obsolete custom rule type, from [#196](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/196), is separate: it covers the field's initializer, where the compiler already warns at your attribute, not the call.

**`[Obsolete(..., error: true)]` is different.** Using a member marked this way raises CS0619, which is not a warning at all: pragma cannot suppress it, unlike CS0612 and CS0618. So the call is left out of the generated file entirely, together with the statement it opens, and ZV0032 reports the compiler's own CS0619 message as an error at the attribute instead. It is not emitted even as `if (false)`, which the compiler would flag as unreachable code there. This covers:

- a property marked `[Obsolete(..., error: true)]`, or whose getter or overridden property is. Every rule on it is left out: a built-in rule that reads it in its condition, a `[Must]` predicate and a custom rule that are given its value as their argument.
- a `[CustomValidation]` method marked `[Obsolete(..., error: true)]`. Its call and the loop over its failures are left out, and the model's other rules and `[CustomValidation]` methods still run.
- a nested or collection property marked `[Obsolete(..., error: true)]`, or whose getter or overridden property is: one whose type or element type is a `[Validate]` model, or that has `[ValidateWith]`. The generated validator would read it to hand it to the nested validator, so its nested or collection validation is left out, and the validator takes no validator for it in its constructor, so the DI registration registers none either. There is no rule attribute to report at, so ZV0032 is reported at the property, made for `nested validation of 'Home'` or `collection validation of 'Homes'`. A rule on the same property, such as `[NotNull]`, still gets its own ZV0032 at its attribute.
- any other call the generated validator makes for a rule that the compiler reports CS0619 on.

A `[Must]` predicate, `When`, `Unless` or `[SkipWhen]` method that is itself `[Obsolete(..., error: true)]` does not compile as a call, so it is reported as [ZV0030](#zv0030) and left out, not as ZV0032.

```csharp
[Validate]
public class Order
{
    [Must(nameof(IsKnownCode))]                  // ZV0032, Error — CS0619, Code is obsolete as an error
    [Obsolete("Use Sku.", error: true)]
    public string Code { get; set; } = "";

    [CustomValidation]                           // ZV0032, Error — CS0619, Check is obsolete as an error
    [Obsolete("Use CheckAll.", error: true)]
    public ValidationFailure[] Check() => [];

    [Obsolete("Use Addresses.", error: true)]
    public Address? Home { get; set; }           // ZV0032, Error, at Home — CS0619, nested validation of 'Home'

    public bool IsKnownCode(string code) => code.Length == 3;
}
```

The fix is to stop validating the obsolete member: move the rule to its replacement, or remove it.

A rule declared on a base type that is itself `[Validate]` is reported once, by that type, when the call warns there too.

**Fix:** Change your code so the call no longer warns. For a nullability warning, let the method accept what it can receive, for example `IsKnownCode(string? code)`, or guard the rule with `When`, or use `[StopOnFirstFailure]` after `[NotNull]`. For an obsolete method or property, call its replacement. If the warning is expected, suppress ZV0032 where you can see it, with `#pragma warning disable ZV0032` around the attribute or `<NoWarn>$(NoWarn);ZV0032</NoWarn>` in the project. Under `TreatWarningsAsErrors`, ZV0032 is then the only thing left to deal with.

---

## ZV0033

**Severity:** Error

**Title:** Numeric comparison rule on a type that is not a number

**When fired:** `[GreaterThan]`, `[GreaterThanOrEqualTo]`, `[LessThan]`, `[LessThanOrEqualTo]`, `[InclusiveBetween]`, `[ExclusiveBetween]`, and `[Equal]` and `[NotEqual]` with a number compare the value as `System.Convert.ToDouble(value)`, because their bounds are `double` attribute arguments. That conversion works for numbers, including `nint` and `nuint`, for `decimal` and enums, for their nullable forms, and for any other type that implements `IConvertible`, such as `string` and `bool`. It always throws `InvalidCastException` for `DateTime` and `char`, and for any type that does not implement `IConvertible`, such as `DateOnly`, `TimeOnly`, `TimeSpan`, `DateTimeOffset`, `Guid`, `object` or your own struct. Such a rule used to compile and then throw for every value that was present. It is now reported at the attribute and left out of the generated validator:

```csharp
[Validate]
public class Booking
{
    [GreaterThan(0)]                 // ZV0033 — DateOnly cannot be converted to a number
    public DateOnly? Arrival { get; set; }

    [InclusiveBetween(1, 14)]        // fine — int
    public int Nights { get; set; }
}
```

> '{Attr}' compares '{Prop}' as a number, but its type '{Type}' cannot be converted to one; use [Must] or a custom ValidationAttribute\<T\> to compare it

For a single-property value object the rule reads the wrapped value, so that value's type is checked. `[Equal("text")]` and `[NotEqual("text")]` compare strings and are not affected.

A value whose type is a type parameter of a [generic model](getting-started.md#generic-models) is compared as `double.CreateChecked(value)`, which does not box a value-type closing. That needs the type parameter constrained to `System.Numerics.INumberBase<T>`, directly or through an interface such as `INumber<T>`. Any other type parameter, including one constrained to `IConvertible`, is reported, and the message then reads:

> '{Attr}' compares '{Prop}' as a number, but its type '{Type}' cannot be converted to one; constrain it to System.Numerics.INumberBase\<T\>, or use [Must] or a custom ValidationAttribute\<T\> to compare it

```csharp
[Validate]
public class Measure<T> where T : INumber<T>
{
    [GreaterThan(0)]                 // fine — double.CreateChecked(instance.Amount) <= 0
    public T Amount { get; set; } = T.One;
}
```

**Fix:** Compare the value with a `[Must]` predicate, or with a custom rule deriving from `ValidationAttribute<T>`, where the bound can be any type:

```csharp
[Validate]
public class Booking
{
    [Must(nameof(IsInTheFuture))]
    public DateOnly? Arrival { get; set; }

    public bool IsInTheFuture(DateOnly? arrival) =>
        arrival is null || arrival > DateOnly.FromDateTime(DateTime.Today);
}
```

See [Custom validation](custom-validation.md) for custom rules.

---

## ZV0034

**Severity:** Error

**Title:** Options validation of a model with asynchronous rules

**When fired:** `ValidateWithZeroAlloc()` is called on an `OptionsBuilder<T>` whose model has an asynchronous rule, an [`AsyncValidationAttribute<T>`](custom-validation.md#asynchronous-rules--asyncvalidationattributet), directly or through a nested or collection `[Validate]` model. Options validation, `IValidateOptions<T>`, is synchronous, so it calls the generated validator's `Validate`, which cannot run an asynchronous rule and throws `NotSupportedException` rather than skip it. Without this error the application would fail when the options are first resolved, or at startup with `ValidateOnStart()`. The error is reported at the call:

```csharp
[Validate]
public class TenantOptions
{
    [KnownTenant]                          // an AsyncValidationAttribute<string?>
    public string? TenantId { get; set; }
}

builder.Services.AddOptions<TenantOptions>()
    .BindConfiguration("Tenant")
    .ValidateWithZeroAlloc();              // ZV0034
```

> '{Model}' has asynchronous validation rules, which options validation cannot run because it is synchronous; validate the options with ValidateAsync where they are used, or move the asynchronous rules off the options model

The call is recognised as `builder.ValidateWithZeroAlloc()`, `builder?.ValidateWithZeroAlloc()` or the static form. Any other route to options validation, such as registering `ZeroAllocOptionsValidator<T>` by hand, still reaches the throwing `Validate`.

**Fix:** Keep the options model synchronous: move the asynchronous check to where the options are used, and validate there with `await validator.ValidateAsync(options)`, or replace the rule with a synchronous one.

---

## ZV0035

**Severity:** Warning

**Title:** [PipelineBehavior] type that does not implement IPipelineBehavior

**When fired:** A class carries `[PipelineBehavior]`, or an attribute deriving from it, but does not implement `ZeroAlloc.Pipeline.IPipelineBehavior`. Only a type that implements the interface joins the pipeline of the generated validators, so this one is left out and its `Handle` method is never called. The most common case is a `static class`, which cannot implement an interface. It used to be dropped without a word. The diagnostic is reported once, at the `[PipelineBehavior]` attribute, whether or not the project has a `[Validate]` model:

```csharp
[PipelineBehavior(Order = 0)]        // ZV0035
public static class LoggingBehavior
{
    public static ValidationResult Handle<TModel>(
        TModel instance, Func<TModel, ValidationResult> next) => next(instance);
}
```

> 'LoggingBehavior' has [PipelineBehavior] but does not implement IPipelineBehavior, so it never runs in a validator's pipeline; make the class non-static and implement IPipelineBehavior

For a non-static class the message ends with "implement IPipelineBehavior". It is a warning rather than an error so that a build that compiled before still compiles.

**Fix:** Make the class non-static and implement `IPipelineBehavior`. `Handle` stays static:

```csharp
[PipelineBehavior(Order = 0)]
public class LoggingBehavior : IPipelineBehavior
{
    public static ValidationResult Handle<TModel>(
        TModel instance, Func<TModel, ValidationResult> next) => next(instance);
}
```

If the class is not meant to be a behavior, remove `[PipelineBehavior]`.

---

## ZV0036

**Severity:** Error

**Title:** Built-in rule on a value whose type is a type parameter

**When fired:** A generic model has one generated validator for all of its closings, so a rule on a property whose type is one of its type parameters must compile for whatever the type parameter is closed over. The rules that read a string, a length or a count have no such form, and are reported at the attribute and left out of the generated validator: `[NotEmpty]`, `[Empty]`, `[MinLength]`, `[MaxLength]`, `[Length]`, `[EmailAddress]`, `[Matches]`, `[IsEnumName]`, `[PrecisionScale]`, and `[Equal]` and `[NotEqual]` with a string. `[IsInEnum]` is reported unless the type parameter is constrained to `struct, Enum`. The value checked is the one the rule reads, after `Nullable<T>` and single-property value-object unwrapping, so `T?` and `Quantity<T>` count too.

```csharp
[Validate]
public class Box<T>
{
    [NotEmpty]                        // ZV0036 — string.IsNullOrEmpty does not take a T
    public T Value { get; set; } = default!;
}
```

> '{0}' cannot validate '{1}': its type '{2}' is a type parameter, so the rule has no form that fits every closing; constrain the type parameter, use [Must] or a custom ValidationAttribute\<T\>

What does work on a type parameter:

| Rule | On a type parameter `T` |
|---|---|
| `[NotNull]`, `[Null]` | always; never fails for a value-type closing |
| `[GreaterThan]` and the other numeric comparisons | when `T` is constrained to `INumberBase<T>`, see [ZV0033](#zv0033) |
| `[IsInEnum]` | when `T : struct, Enum`, as `Enum.IsDefined<T>(value)` |
| `[Must]` | always; the method takes the value as declared |
| a custom `ValidationAttribute<TValue>` | when `T` converts to `TValue`, such as `object?` or an interface `T` is constrained to, see [ZV0021](#zv0021) |

A possibly-null `T`, one without a `struct` or `unmanaged` constraint, is tested for null before it is compared, and a custom rule declared never to receive null, such as `ValidationAttribute<object>`, reports ZV0021: declare it as `ValidationAttribute<object?>`.

**Fix:** Validate the value with `[Must]` or a custom `ValidationAttribute<T>`, or constrain the type parameter where the rule has a constrained form.

---

## ZV0037

**Severity:** Error

**Title:** Nested model that nests its model inside itself without end

**When fired:** A property of a generic model is validated by the generated validator of its own `[Validate]` model, closed over its type arguments. When that closing grows on every round of nesting, every validator takes the validator of a larger closing, without end, so none of them could be constructed or registered:

```csharp
[Validate]
public class Node<T>
{
    [NotEmpty] public string Name { get; set; } = "";

    public Node<Node<T>>? Next { get; set; }  // ZV0037 — Node<T>, Node<Node<T>>, Node<Node<Node<T>>>...
    public Node<T>? Parent { get; set; }      // fine — the same closing again
}
```

The chain can run through other models too: `A<T>` holding a `B<List<T>>`, with `B<U>` holding an `A<U>`, grows by one `List` on every round, and `A<T>`'s property is reported. A property that reorders or replaces the type arguments, such as `Pair<U, T>` inside `Pair<T, U>` or `Node<string>` inside `Node<T>`, reaches finitely many closings and is composed.

> '{0}' nests '{1}' inside itself, so its validators would form an unbounded chain; validate it with [ValidateWith] or a [Must] rule

It is reported at the property, which is not validated as a nested model. The validator takes no validator for it.

**Fix:** Validate the property with `[ValidateWith]` and a validator of your own, or check it with a `[Must]` rule.

---

## ZV0038

**Severity:** Warning

**Title:** Pipeline behavior applied to a closed form of a generic model

**When fired:** A generic `[Validate]` model has one generated validator for all of its closings, so a pipeline behavior applies to every closing or to none. `AppliesTo` naming one closing matches no validator, and the behavior never runs. It is reported at the `[PipelineBehavior]` attribute:

```csharp
[PipelineBehavior(AppliesTo = typeof(Page<Order>))]   // ZV0038
public sealed class AuditOrderPages : IPipelineBehavior { ... }
```

> 'AuditOrderPages' applies to 'Ns.Page\<Ns.Order\>', a closed form of the generic model 'Ns.Page\<TItem\>'; a behaviour runs for every closing of a generic model, so name it as typeof(Page\<\>)

It is a warning, not an error: the same code compiled before and ran no behavior either.

**Fix:** Name the model's open form. It runs in `PageValidator<TItem>` for every closing:

```csharp
[PipelineBehavior(AppliesTo = typeof(Page<>))]
public sealed class AuditPages : IPipelineBehavior { ... }
```

A model declared inside a generic type is named the same way: `typeof(Outer<>.Inner<>)` or `typeof(Outer<>.Plain)`.

---

## Release tracking

`src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Shipped.md` records the release each analyzer rule first shipped in, and any later change to its category or severity. A new rule goes into `AnalyzerReleases.Unshipped.md`. Changing a shipped rule's severity or category, or removing it, has to be declared there under `### Changed Rules` or `### Removed Rules`, or the build fails. The same move covers every `PublicAPI.Unshipped.txt`: new public API goes there, and removing shipped API is declared with a `*REMOVED*` line.

Nobody moves entries by hand. When release-please opens or updates the release PR, the `ship-release-tracking` job in `.github/workflows/release-please.yml` moves everything unshipped into the Shipped files on that branch, in a `chore: mark analyzer rules and public api shipped in <version>` commit. The `release-tracking` job in CI fails a release PR while anything is still unshipped. Both use the shared [`ship-release-tracking.py`](https://github.com/ZeroAlloc-Net/.github/blob/main/scripts/ship-release-tracking.py). **Before merging a release PR,** check that it has that commit. If it doesn't, run the script with the release version from the root of the release branch and push the result.
