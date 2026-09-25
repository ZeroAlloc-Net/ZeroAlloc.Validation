# Custom rule attributes: design

**Issue:** #196. **Release:** ships in 2.0.0, held in release PR #199. **Status:** approved 2026-09-25.

## Problem

`ValidationAttribute` is public and can be subclassed, but `RuleEmitter.IsRuleAttribute` recognises only a fixed list of built-in attribute names. A user-defined subclass is **silently ignored**, so the property it decorates goes unvalidated and nothing says so. There is also no supported way to write a reusable rule such as `[NotBlank]` that the generator emits as a statically bound check.

## Goals

- A documented extension point for reusable property-level rule attributes.
- The check is a statically bound call. No reflection or `Activator`, no per-validation allocation, and safe under trimming and NativeAOT.
- Custom rules keep the existing rule behaviour for `Message`, `ErrorCode`, `Severity`, `When`/`Unless` and stop-on-first-failure.
- An attribute the generator cannot emit fails the build, instead of being silently accepted.

## Non-goals

- **Async rules.** A later, additive `AsyncValidationAttribute<T>` with `ValueTask<bool> IsValidAsync(T, CancellationToken)` is tracked in #202. `ValidationAttribute<T>` must never gain an async member, because adding one later would be a breaking change.
- **Per-element rules on collections.** A custom rule applies to the property value, the same as the built-in rules on a collection property.

## Public API

Both types live in `ZeroAlloc.Validation`, in the `ZeroAlloc.Validation` namespace.

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class ValidationAttribute<T> : ValidationAttribute
{
    public abstract bool IsValid(T value);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RuleMessageAttribute(string message) : Attribute
{
    public string Message { get; } = message;
    public string? ErrorCode { get; set; }
}
```

`Message`, `When`, `Unless`, `ErrorCode` and `Severity` are inherited from `ValidationAttribute` unchanged.

Example:

```csharp
[RuleMessage("{PropertyName} must not be blank.", ErrorCode = "NOT_BLANK")]
public sealed class NotBlankAttribute : ValidationAttribute<string?>
{
    public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
}

[RuleMessage("{PropertyName} must have at least {minWords} words.")]
public sealed class MinWordsAttribute(int minWords) : ValidationAttribute<string?>
{
    public int MinWords { get; } = minWords;
    public override bool IsValid(string? value) => /* ... */;
}
```

## Generator

### Recognition

A rule attribute is either a built-in, which works as today, or an attribute whose class derives from `ValidationAttribute<T>`, directly or through intermediate base classes. `T` is taken from the closed `ValidationAttribute<T>` found by walking the base-type chain.

Every call site of `IsRuleAttribute` must accept custom rules, so that each code path sees them:
- the main emit loop
- the direct-return emit loop
- `HasReachableCondition`, where ZV0017 applies to custom rules too
- `GetUnreachableConditionMethods`

### Emission

- **Static field.** Each custom-rule usage becomes one `private static readonly` field on the generated validator, for example `__rule0` or `__rule1`. The name is unique within the validator.
  - **Initializer:** `new global::Full.Type.Name(ctorArgs) { NamedProp = value, ... }`, rebuilt from the usage's `AttributeData`.
  - **Constructor arguments:** in order, emitted with `TypedConstant.ToCSharpString()`, which covers primitives, strings, chars, enums, `typeof` and arrays.
  - **Named arguments:** the base members `Message`, `When`, `Unless`, `ErrorCode` and `Severity` are *not* copied into the instance. The emit loop already reads them at compile time. Any other named argument is copied.
  - **Base members are recognised by symbol, not by name.** A named argument is a base member only when it binds a member declared on `ZeroAlloc.Validation.ValidationAttribute`, resolved from the most derived type up, the way C# binds it. An attribute's own `new string? Message` is therefore an ordinary named argument: it is copied into the instance and does not set the failure message. The same holds for `When`, `Unless`, `ErrorCode` and `Severity`.
  - **Obsolete symbols:** when the initializer names an `[Obsolete]` symbol, the field declaration alone is wrapped in `#pragma warning disable CS0618, CS0612` and the matching `restore`, under the generated comment `// The rule type is obsolete; the compiler already warns at the attribute usage in user code.` The symbol can be the rule attribute type or a type containing it, its constructor, a named member it sets, or a `typeof` or enum argument type. The user sees the warning at the usage, where they can act on it. The repeat inside generated code cannot be suppressed from user code and would break a `TreatWarningsAsErrors` build. This is the one ruled exception to "no suppressions in generated code". Every other field, and all built-in output, gets no pragma.
  - **Generic attribute types:** a closed usage such as `[InRange<int>(1, 5)]` is emitted with its fully qualified closed type name.
- **Condition:** `!__ruleN.IsValid(<raw property access>)`. It uses the raw, un-unwrapped access, the same as `[Must]`, so a value-object property passes the wrapper type.
- **The rest of the emit loop is unchanged:** the `When`/`Unless` guards, the stop-on-first-failure `else if` chain, `Severity`, `ErrorCode` and the failure initializer.
- **Allocation:** one instance per usage, created when the validator type initialises. A passing validation allocates nothing per call, with two exceptions. A value-type property checked by a rule whose `T` is a reference type, such as `object` or an interface, is boxed on every call. A user-defined implicit conversion to `T` runs on every call. A failing validation allocates its result, as a built-in rule does. `AllocationRegressionTests` pins the valid path of a custom-rule model at zero bytes.
- **Sharing:** the one instance serves every call on every thread, so `IsValid` must be stateless and thread-safe.
- **Accessibility:** the generated validator is a separate class in the model's assembly, in its own generated file. A usage can compile while its attribute type or constructor is not reachable from that class, for example a `private` or `protected` attribute nested inside the model, which would give CS0122 in generated code. The generator checks accessibility with `Compilation.IsSymbolAccessibleWithin` against the assembly. It treats every `file`-local type as inaccessible, whether it is the attribute type, a containing type or an argument type. It reports ZV0023 instead of emitting the rule.
- **Where rules are read:** from properties only. A rule attribute may widen its own `[AttributeUsage]`, so it can compile on a field or a constructor parameter, including a record's positional parameter without the `property:` target. Every `ValidationAttribute` subclass there is reported as ZV0024 at the attribute. Fields and constructor parameters are searched on the `[Validate]` type and on each base type whose properties it validates. The search stops at a base type that is itself `[Validate]`, because that type reports its own.

### Messages

Precedence:
1. `Message` on the usage, which is today's behaviour.
2. `[RuleMessage]` on the attribute class, found by walking base types, nearest first.
3. The fallback `"{PropertyName} is invalid."`, the same as `[Must]`.

`ErrorCode` precedence is the same: the usage's `ErrorCode`, then `[RuleMessage].ErrorCode`, then none. An `ErrorCode` written on the usage wins even when it is `null`, so `ErrorCode = null` clears the code that `[RuleMessage]` declares.

`[RuleMessage]` is resolved once per usage and serves both the message and the error code. The fallback text is one constant shared with `[Must]`.

Placeholders other than `{PropertyValue}` are resolved at compile time into a constant string:
- `{PropertyName}` and `{PropertyValue}` have their existing meaning. `{PropertyValue}` is formatted at validation time, and only when the rule fails.
- `{name}` is replaced by the constructor argument whose **parameter name** matches, or by the named property of that name, as written on the usage. Constant formatting is invariant-culture, matching the existing `{ComparisonValue}` handling.
- Matching is case-sensitive. For `[MinWords(3)]`, `{minWords}` binds the constructor parameter. `{MinWords}` binds only if `MinWords` is written as a named argument on the usage; the generator never reads a property's runtime value. Otherwise it is unknown and reports ZV0022.

These rules apply to messages from both sources: the usage's `Message` and `[RuleMessage]`.

## Diagnostics

The generator's highest ID is ZV0019, from #201. ZV0023 was added during implementation: a private nested attribute compiled but produced CS0122 in generated code. ZV0024 was added in the final review: a rule with a widened `[AttributeUsage]` on a field or record parameter was dropped silently.

| ID | Severity | Condition | Message gist |
|---|---|---|---|
| ZV0020 | Error | An attribute on a property of a `[Validate]` model derives from `ValidationAttribute`, but it is neither a built-in nor a `ValidationAttribute<T>`. | `'{Attr}' derives from ValidationAttribute but the generator cannot emit it; derive from ValidationAttribute<T> and override IsValid.` |
| ZV0021 | Error | The property type has no implicit conversion to the rule's `T`, checked with `Compilation.ClassifyConversion(...).IsImplicit`. | `'{Attr}' validates '{T}' but property '{Prop}' is '{PropType}'.` |
| ZV0022 | Warning | A message placeholder matches neither `PropertyName`, `PropertyValue`, a constructor parameter name nor a named property, on a custom-rule usage. | `Placeholder '{x}' in the message for '{Attr}' on '{Prop}' does not match any argument; it is emitted literally.` |
| ZV0023 | Error | The attribute type, its constructor, or a named property it sets is not accessible from the generated validator, which is a separate class in the same assembly. A `file`-local type is never accessible. | `'{Attr}' is not accessible from the generated validator; make the attribute type and its constructor internal or public.` |
| ZV0024 | Error | A `ValidationAttribute` subclass is applied to a field or a constructor parameter of a `[Validate]` type, where the generator does not read it. | `'{Attr}' is applied to '{Target}', which the generator does not validate; apply it to a property` |

- **Location:** ZV0020 to ZV0024 are all reported at the attribute usage, so a property with several rules points at the offending one.

- **ZV0020 is the breaking change.** A project that compiled with a silently ignored subclass now fails to build. That is the intent: the property was never validated.
- **ZV0021:** the rule is not emitted. The build already fails on the error, so nothing unvalidated ships.
- **ZV0024 is breaking too.** It must ship as an Error in 2.0: added after 2.0, it could only be a Warning.
- **Registration:** each ID goes in `AnalyzerReleases.Unshipped.md` and in `docs/diagnostics.md`.

## Tests

- **Generator snapshot and diagnostic tests:**
  - `[NotBlank]` with no arguments, the issue's own example
  - constructor and named arguments of every constant kind: primitives, strings, chars, enums, `typeof`, arrays and null
  - an indirect base, such as `MyBase : ValidationAttribute<string?>`
  - a closed generic attribute
  - interaction with `Message`, `When`/`Unless`, `ErrorCode`, `Severity` and stop-on-first-failure, on both the buffered and the direct-return emit paths
  - a value-object property
  - message precedence and placeholders
  - ZV0020, ZV0021, ZV0022, ZV0023 and ZV0024, including `file`-local types for ZV0023
  - an explicit `ErrorCode = null`, a `new Message` hiding the base member, and the `[Obsolete]` pragma
  - ZV0017 for a custom rule whose `When` cannot be reached
- **Runtime tests:** models using `[NotBlank]` and `[MinWords(3)]` validate correctly, including null, empty and whitespace-only strings.
- **Allocation tests:** a custom-rule model allocates nothing on the valid path, and only its result on a single failure.
- **PackSmoke:** a consumer defines a custom rule against the packed package and validates with it.
- **Existing snapshots stay byte-identical.** No built-in rule's output changes.

## Docs

- `docs/custom-validation.md`: a new section on custom rule attributes, covering the base class, `[RuleMessage]`, placeholders, allocation and AOT behaviour, and when to use `[Must]` instead.
- `docs/diagnostics.md`: ZV0020 to ZV0024.
- `docs/migrating-to-v2.md`: entries for ZV0020 and ZV0024.

## Delivery

- **One PR.** It is a `feat!:` commit with a `BREAKING CHANGE:` footer describing ZV0020, ZV0023 and ZV0024, and `Closes #196`.
- **Commit message rules:** body lines are at most 100 characters, with no nested parentheses.

## Follow-ups

- Async rule attributes, `AsyncValidationAttribute<T>`: #202. It needs a real `ValidateAsync` emit path, plus a decision on what sync `Validate` does for a model with async rules.
