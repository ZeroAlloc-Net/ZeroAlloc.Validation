# Generic `[Validate]` models: design

**Date:** 2026-09-30
**Issue:** #238. **Status:** proposed, for maintainer review. No implementation starts before the
recommendations below are approved or changed.
**Scope:** generate a validator for a generic `[Validate]` model, and for one declared inside a
generic type, and make the Inject, Options and ASP.NET Core glue and nested composition work with
it. Today such a model reports **ZV0029** and gets no validator, issue #219.

## Background

`GeneratedValidatorReach.IsGeneric` returns true for a model with type parameters, or one inside a
type that has them, and `HasGeneratedValidator` is false for it. Every generator reads that one
check:

- `ValidatorGenerator.ReportModelWithoutValidator` reports ZV0029 and emits nothing.
- `InjectGenerator`, `OptionsValidationEmitter` and `AspNetCoreFilterEmitter` drop the model in their
  `ForAttributeWithMetadataName` transform.
- `ValidatorDependencies.HasGeneratedValidator` keeps a property of that type out of nested
  composition, so it is not validated as a nested model.
- `MethodCallProbe.ValidatedTypes` skips it with `!type.IsGenericType`.

Every emitter assumes that the model and every type it writes into generated code are closed types.
Each design point below lists where that assumption sits in the code.

## Goals

- A generic model gets one generated validator, generic over the model's type parameters, with the
  same rules, messages and behaviours as a non-generic model.
- A closed form such as `Page<Order>` is validated wherever a non-generic model is: nested,
  in a collection, through DI, through Options and through the ASP.NET Core filter.
- No reflection, no `MakeGenericType`, no allocation on the valid path, and safe under trimming and
  NativeAOT, for value-type and reference-type closings alike.
- A rule the generator cannot emit for a type parameter fails the build with a diagnostic. It must
  never be dropped silently, and it must never become a compiler error inside generated code.
- Shippable in minor releases: relaxing ZV0029 only lifts an error.

## Non-goals

- Generic **rule attributes that use the model's type parameter**, such as `[InRange<T>]`. C#
  rejects these with CS8968, "an attribute type argument cannot use type parameters". The same
  goes for `[ValidateWith(typeof(X<T>))]`, which C# rejects with CS0416.
- Resolving a **validator for a type parameter at runtime**, such as validating a `T Item` property
  with whatever `ValidatorFor<T>` the container happens to hold. D-2 covers this.
- Per-closing specialisation, such as different rules for `Page<Order>` and `Page<Customer>`.

## What was verified

Each claim below was checked against the code at `origin/main` 824d37d. The language, Roslyn and
DI behaviour was checked with a throwaway Roslyn 5.9.0 and Microsoft.Extensions.DependencyInjection
9.0.9 probe.

| Claim | Result |
|---|---|
| MS DI open generic `typeof(ValidatorFor<>)` to `typeof(PageValidator<>)` | Registration succeeds. Resolving the closing throws `ArgumentException: Implementation type 'BoxV`1[Box2`1[System.Int32]]' can't be converted to service type 'VF`1[Box2`1[System.Int32]]'`, with the probe's stand-ins `VF<>`, `BoxV<>` and `Box2<>` for `ValidatorFor<>`, `PageValidator<>` and `Page<>`. Resolving any other `ValidatorFor<X>`, such as `ValidatorFor<string>`, **also throws** instead of returning null. |
| MS DI open generics under NativeAOT | The DI assembly contains `VerifyOpenGenericAotCompatibility` and the resource `AotCannotCreateGenericValueType`. An open-generic resolution with a value-type argument fails when dynamic code is not supported. |
| `ITypeParameterSymbol.AllInterfaces` for `where U : IConvertible` | **Empty.** `ConstraintTypes` holds `IConvertible`. `RuleEmitter.ConvertsToDouble`, `HasCountProperty` and `ImplementsEnumerable` read `AllInterfaces`, so all three return false for a constrained type parameter. |
| `IsReferenceType` of an unconstrained `T` | false, and `IsValueType` is false too. `RuleEmitter.CanBeNull` therefore adds no null guard, although a reference-type closing can be null. |
| `ClassifyCommonConversion(T, object)` and `(T, int)` | Implicit and none. ZV0021 works on `T` unchanged. |
| `typeof(Page<>)` in `[PipelineBehavior(AppliesTo = ...)]` | Displays as `global::Ns.Page<>`, and `typeof(Outer<>.Inner<>)` as `global::Ns.Outer<>.Inner<>`. The model itself displays as `global::Ns.Page<T>`, so `BehaviorDiscoverer.ForModel`'s ordinal string compare never matches. |
| `ns.GetTypeMembers("Box")` with both `Box` and `Box<T>` declared | Returns both. `GeneratedValidatorReach.ValidatorNameClashes` would report a false ZV0031 between them once generics get validators. |
| `Outer<T>.Inner<T>` | Compiles with warning CS0693. A validator `Outer_InnerValidator<T, T>` fails with CS0692. |
| `x is null`, `x is not null` and `x?.ToString()` on an unconstrained `T` | Compile. |
| `string.IsNullOrEmpty(t)`, `t.Length` on a `T` | CS1503 and CS1061. That is today's `[NotEmpty]` fallback and the length rules. |
| `double.CreateChecked(v)` for `T : INumberBase<T>`, also on `T?` | Compiles. Every supported TFM, net8.0 to net10.0, has generic math. |
| `System.Convert.ToDouble(v)` for `T : IConvertible` | Binds to `Convert.ToDouble(object)`, so a value-type closing is boxed on every call. |
| `Enum.IsDefined<T>(v)` for `T : struct, Enum` | Compiles. |
| Extension overloads `V<T>(this OB<Box<T>>)`, `V<T>(this OB<Crate<T>>)` and `V(this OB<Plain>)` in one class | Compile together. Inference picks the right one for `OB<Box<int>>`, `OB<Crate<string>>` and `OB<Plain>`. |
| `Handle<global::Ns.Box<T>>(instance, static r1 => ...)` inside a generic class | Compiles. |
| CS1712 | Reported only when some type parameters have a `<typeparam>` tag and others do not. |
| Hint names `Ns.BoxValidator`1.g.cs` and `Ns.BoxValidator{T}.g.cs` | Both accepted by `AddSource`. |
| ZeroAlloc.Inject with a lifetime attribute on a generic class | Registers the open type through a `ServiceDescriptor` with `typeof(X<>)`. In standalone mode it enumerates closings from constructor parameters, and reports ZAI018 when there are none. |
| ZeroAlloc.Mediator `ValidationBehavior` | Resolves `sp.GetService<ValidatorFor<TRequest>>()` and **skips validation on null**. |

## Decisions

Each decision lists its options, what each one generates, and one recommendation. The example
models are:

```csharp
namespace Ns;

[Validate]
public class Page<TItem> where TItem : class
{
    [NotEmpty] public string Title { get; set; } = "";
    [NotNull]  public TItem? Selected { get; set; }
    public List<Line<TItem>> Lines { get; set; } = [];
}

[Validate]
public class Line<TItem> where TItem : class
{
    [GreaterThan(0)] public int Quantity { get; set; }
    [NotNull] public TItem? Item { get; set; }
}

[Validate]
public class OrderPage
{
    public Page<Order> Page { get; set; } = new();
}
```

---

### D-1: validator shape

**Option A, recommended: one validator, generic over every type parameter of the model and of
each type containing it, outermost first, with the declared names and constraints.**

```csharp
/// <summary>Validates <c>Page</c> instances against the rules declared on the type.</summary>
/// <typeparam name="TItem">The <c>TItem</c> type parameter of the validated model.</typeparam>
public sealed partial class PageValidator<TItem> : ValidatorFor<global::Ns.Page<TItem>>
    where TItem : class
{
    private readonly global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Line<TItem>> _linesValidator;

    /// <summary>Initialises a new <c>PageValidator</c> with the validators for its nested members.</summary>
    /// <param name="linesValidator">The validator for the nested <c>Lines</c> member.</param>
    public PageValidator(global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Line<TItem>> linesValidator)
    {
        _linesValidator = linesValidator;
    }

    /// <inheritdoc/>
    public override global::ZeroAlloc.Validation.ValidationResult Validate(global::Ns.Page<TItem> instance)
    { /* the rules, exactly as for a non-generic model */ }
}

// Nested in a generic container: public class Envelope<T> { [Validate] public class Header { ... } }
public sealed partial class Envelope_HeaderValidator<T> : ValidatorFor<global::Ns.Envelope<T>.Header> { ... }
```

- **Name:** `GeneratedValidatorNames.ValidatorName` is unchanged, `PageValidator` and
  `Envelope_HeaderValidator`. The arity sets it apart, so `Box` and `Box<T>` can live side by side as
  `BoxValidator` and `BoxValidator<T>`.
- **Arity in the other names.** `HintName` becomes `Ns.PageValidator`1.g.cs`. Without the suffix,
  `Box` and `Box<T>` would both add `Ns.BoxValidator.g.cs`, and the generator would throw.
  `MetadataName` becomes `Ns.PageValidator`1`, which `ValidatorRegistrationEmitter.ReferencedValidator`
  needs for `GetTypeByMetadataName`. `QualifiedValidatorName` takes the type argument list, the
  declared names or the closing's fully qualified arguments, see D-4.
- **ZV0031 compares total arity.** Total arity is the model's own arity plus that of every
  containing type. `ValidatorNameClashes` must drop a candidate whose total arity differs, or `Box`
  and `Box<T>` report a false clash. `Outer<T>.Inner` against a top-level `Outer_Inner<T>` still
  clashes, because both produce `Outer_InnerValidator<T>`.
- **Declared names.** The validator redeclares the model's names, so every existing
  `ToDisplayString(FullyQualifiedFormat)` call, such as `global::Ns.Page<TItem>` or
  `global::Ns.Line<TItem>`, is already valid inside the validator. No emitter needs a type renderer.
- **Constraints** are copied clause by clause from `ITypeParameterSymbol`: `class`, `class?`,
  `struct`, `unmanaged`, `notnull`, the constraint types with their nullable annotations,
  `new()` and `allows ref struct`. They are written fully qualified. The model is valid C#, so
  its constraint types are already at least as accessible as the model. The validator follows the
  model's accessibility, so no extra accessibility check is needed.
- **Documentation:** one `<typeparam>` per type parameter, all or none, because a partial set raises
  CS1712. The `<summary>` keeps `DocumentationNameFormat`, which has no generics, so no raw `<`
  reaches the XML and raises CS1570.
- **Lifetime attribute:** `[Transient]`, `[Scoped]` or `[Singleton]` on the model is still copied.
  ZeroAlloc.Inject then registers the open validator type as itself. Nothing generated here
  resolves the validator by its own type, so that registration stays inert, and the AOT caveat
  goes in the docs. D-4 has the details.
- **ZV0029 is narrowed, not removed.** It stays for the one shape Option A cannot express: a type
  parameter whose name repeats along the containing chain, the CS0693 case `Outer<T>.Inner<T>`,
  or one named like the validator itself, which would raise CS0694. The message becomes
  "'{0}' declares type parameter '{1}' more than once along its containing types, so no validator
  is generated; rename one of them". CS0693 already warns about the declaration, so the fix is
  obvious.

**Option B: one validator per closing, such as `Page_OrderValidator`, for each closing found in the
compilation.** This is rejected:

- A closing in another assembly never gets one. A library that ships `Page<T>` cannot know its
  consumers' closings. The consumer's generator would have to rebuild the rules of a type from
  metadata, including internal members it cannot reach.
- It duplicates the body for every closing, and adds a naming scheme for type argument lists.
- The generic validator still has to exist for a use such as `new PageValidator<TItem>()` inside
  other generic code.

**Option C: Option A with type parameters always renamed, such as `T0` and `T1`, or with only
duplicates renamed.** This removes the CS0693 limit. Every emitter would then need a type renderer
that rewrites type parameters inside property types, constraint types, nested validator fields and
probe code. The codebase has dozens of `ToDisplayString(FullyQualifiedFormat)` sites. This is
rejected: that is a lot of risk for a declaration the compiler already warns about.

**NativeAOT:** Option A is safe. A value-type closing gets its own compiled instantiation. The
instantiation is rooted by the closed registration or by `new` in code that the compiler sees,
see D-4. Reference-type closings share canonical code. Option B is safe too. Option C is the same
as A.

---

### D-2: properties whose type is a type parameter

The **operand** is the value a rule reads, after `Nullable<T>` and single-property `[ValueObject]`
unwrapping. The policy applies when the operand is an `ITypeParameterSymbol`. A property of a
constructed type such as `List<TItem>` or `Line<TItem>` is not affected: its shape is known, and
the existing code handles it.

**Option A, recommended: support a rule on a type-parameter operand only where it has a
zero-allocation form that follows from the constraints. Otherwise report it at the attribute and
leave it out.**

| Rule | Type-parameter operand | Generated condition |
|---|---|---|
| `[NotNull]`, `[Null]` | always | `instance.Selected is null`, which is legal for unconstrained `T` and always false for a value-type closing |
| `GreaterThan`, `LessThan`, the `…OrEqualTo` pair, `InclusiveBetween`, `ExclusiveBetween`, numeric `Equal` and `NotEqual` | when `T` is constrained to `System.Numerics.INumberBase<T>`, directly or through `INumber<T>` or another interface that inherits it | `instance.Amount is not null && (double.CreateChecked(instance.Amount) <= 0)`; the null guard is dropped when `T` has a `struct` or `unmanaged` constraint |
| the same, any other `T`, including `T : IConvertible` | **ZV0033**, rule left out | the hint names `INumberBase<T>` |
| `[IsInEnum]` | when `T : struct, Enum` | `!global::System.Enum.IsDefined<T>(instance.Kind)` |
| `[NotEmpty]`, `[Empty]`, `[MinLength]`, `[MaxLength]`, `[Length]`, `[EmailAddress]`, `[Matches]`, `[IsEnumName]`, `[PrecisionScale]`, string `[Equal]` and `[NotEqual]` | **new ZV0036**, Error, rule left out | none |
| `[Must]` | as today | `!instance.IsKnown(instance.Selected)`. The method's parameter type binds to `T`, and CS errors are mirrored through the probe, see D-7 |
| custom `ValidationAttribute<TValue>` | as today, through ZV0021 | `!__rule0.IsValid(instance.Selected)`. The rule is emitted when `T` converts implicitly to `TValue`: to `object` or to a constraint interface. That conversion boxes a value-type closing on every call, the case `docs/custom-validation.md` already documents for a reference-type `TValue`. `T` to `int` still reports ZV0021 |
| `{PropertyValue}` in a message | always | `(instance.Selected is null ? "null" : System.Convert.ToString(instance.Selected, System.Globalization.CultureInfo.InvariantCulture))`. It runs on the failure path only, which already allocates. `Convert.ToString(null)` returns `""`, hence the explicit null arm |

ZV0036: "'{0}' cannot validate '{1}': its type '{2}' is a type parameter, so the rule has no
form that fits every closing; constrain the type parameter, use [Must] or a custom
ValidationAttribute<T>". It is an Error for the same reason as ZV0033: the only alternative is a
compiler error in generated code, or a silently dropped rule.

Where each supporting change goes in the code:

- `ConvertsToDouble` must not read `AllInterfaces` for a type parameter, which is always empty. A
  new `IsGenericNumber(ITypeParameterSymbol)` walks `ConstraintTypes` and their `AllInterfaces` for
  `System.Numerics.INumberBase<TSelf>` with `TSelf` equal to the parameter. `CompareValue` emits
  `double.CreateChecked(v)` for it, and `CanCompareAsNumber` reports ZV0033 for every other type
  parameter.
- `CanBeNull` and `CustomRules`' `AcceptsNullRuleDoesNot` treat a type parameter without a `struct`
  or `unmanaged` constraint as possibly null. Otherwise a `Page<string?>` passes null into
  `CreateChecked` or into a rule declared never to receive it, the same defect class as #276.
- `BuildNotEmptyCondition` and the length rules are never reached for a type-parameter operand.
  ZV0036 gates them first, so their `string.IsNullOrEmpty` fallback, which does not compile for `T`,
  is not hit.
- `BuildPropertyValueExpr` gets the type-parameter arm above. Its "any other reference type" arm
  would miss the value-type closing.

**Option B: emit a runtime type switch**, such as
`v switch { string s => string.IsNullOrEmpty(s), ICollection c => c.Count == 0, _ => false }`. A
pattern test of a value-type `T` against an interface boxes it. The rule's meaning then depends on
the closing, and the `_` arm silently passes whatever it does not recognise. This is rejected.

**Option C: resolve every rule through the constraints**, for example `[NotEmpty]` on
`T : ICollection<int>` would emit `.Count`, and `[MinLength]` on a `T` that exposes `Length`. It is
correct, but it touches every rule builder, which read `AllInterfaces` and `GetMembers` of the
declared type today. It is deferred, not rejected. Each rule that gains a constraint-based form
later only lifts a ZV0036 error, so it can ship in a minor.

**Option D: `EqualityComparer<T>.Default.Equals(v, default)` for `[NotEmpty]`**, the
FluentValidation meaning of empty for a generic. This is rejected: `Page<string>` would then accept
`""`, which contradicts the rule's meaning on a `string` property.

**Value-object unwrapping:** `GetValueObjectUnwrapMember` already works on a constructed type. A
generic single-property `[ValueObject] Quantity<T>` unwraps to its `T` member, and the table above
applies to that member. A bare `T` is never unwrapped, even when its class constraint is a value
object: the closing may be a subclass with other public properties. ZV0016 is not reported for a
bare `T`.

**Nested and collection validators** are covered in D-7: a bare `T` is composed only through a
`[Validate]` class constraint.

**NativeAOT:** Option A is safe. `double.CreateChecked` is a constrained call to a static abstract
member, and `Enum.IsDefined<T>` is generic. Both are compiled per instantiation, with no reflection
and no boxing. The `Convert.ToDouble(object)` path, which Option A rejects, is AOT-safe but boxes.
Option B has no AOT problem, but it allocates. Option C is the same as A.

---

### D-3: pipeline behaviours on a generic model

`BehaviorDiscoverer.ForModel` compares `PipelineBehaviorInfo.AppliesTo`, the fully qualified
string of `typeof(X)` as ZeroAlloc.Pipeline writes it, with the model's fully qualified name, using
`string.Equals` with ordinal comparison. For `Page<TItem>` that name is `global::Ns.Page<TItem>`.
`typeof(Page<>)` is `global::Ns.Page<>` and `typeof(Page<Order>)` is `global::Ns.Page<global::Ns.Order>`.
Neither matches.

**Option A, recommended: match the open form, and report a closed one.**

- `ForModel` takes the model's unbound name as well, rendered with `<>` or `<,>` for each generic
  type in the containing chain: `global::Ns.Page<>`, `global::Ns.Outer<>.Inner<>`,
  `global::Ns.Outer<>.Plain`. `ConstructUnboundGenericType` cannot produce the last one, because
  `Plain` has arity 0, so the name is built by walking the containing types. A behaviour applies
  when `AppliesTo` is null, or equals the unbound name.
- A behaviour whose `AppliesTo` is a closed form of a generic `[Validate]` model, such as
  `typeof(Page<Order>)`, reports **new ZV0038**, Warning, at its `[PipelineBehavior]` attribute:
  "'{0}' applies to '{1}', a closed form of the generic model '{2}'; a behaviour runs for every
  closing of a generic model, so name it as typeof({3})". It applies to no validator. That is
  today's outcome too, but today it is silent.
- The chain is emitted unchanged, with `TypeArguments = ["global::Ns.Page<TItem>"]`.
  `Behavior.Handle<global::Ns.Page<TItem>>(instance, static r1 => ...)` compiles inside the generic
  validator.

```csharp
[PipelineBehavior(AppliesTo = typeof(Page<>))]      // runs in PageValidator<TItem> for every TItem
public sealed class AuditPages : IPipelineBehavior { ... }

[PipelineBehavior(AppliesTo = typeof(Page<Order>))] // ZV0038: never runs
public sealed class AuditOrderPages : IPipelineBehavior { ... }
```

**Option B: branch per closing inside the generic validator,** as in
`if (typeof(TItem) == typeof(global::Ns.Order)) return Audit.Handle(...)`. The JIT and NativeAOT
fold that test only for value-type closings. Reference-type closings share canonical code and test
at runtime on every call. It also makes one validator mean different things per closing. This is
rejected.

**Option C: global behaviours only, and any `AppliesTo` ignored for generic models.** This silently
drops a behaviour the user scoped on purpose. It is rejected.

PR #293, which adds ZV0035, also changes `BehaviorDiscoverer.ResolveSymbol`. The implementation
rebases on it.

**NativeAOT:** A is safe, with static calls as today. B is safe, but costs a runtime type test on
reference-type closings. C is safe.

---

### D-4: DI registration in Inject

A registration for `ValidatorFor<Page<TItem>>` cannot be open. MS DI's open-generic support maps
the service's type arguments to the implementation's positionally. `ValidatorFor<>` to
`PageValidator<>` would turn `ValidatorFor<Page<int>>` into `PageValidator<Page<int>>`. The probe
confirms that it throws, and that the same registration makes **every** other `ValidatorFor<X>`
lookup throw instead of returning null. That would break ZeroAlloc.Mediator's
`ValidationBehavior`, which relies on `GetService` returning null to skip validation.

**Options:**

- **A: open-generic `ServiceDescriptor`.** It cannot work, as described above. Rejected.
- **B: closed registrations for the closings the registration graph reaches.** The graph is
  `ValidatorRegistrationEmitter.RegistrationGraph`, and it reaches every nested and collection
  closing of every registered model:

  ```csharp
  // AddZeroAllocValidators(), for OrderPage above
  services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.OrderPage>, global::Ns.OrderPageValidator>();
  services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Page<global::Ns.Order>>, global::Ns.PageValidator<global::Ns.Order>>();
  services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Line<global::Ns.Order>>, global::Ns.LineValidator<global::Ns.Order>>();
  ```

  This is complete for composition: a composed validator's constructor only ever takes closings
  that the graph walks. It is incomplete for a closing used only at the root, such as an action
  argument `Page<Customer>`, a `GetRequiredService<ValidatorFor<Page<Customer>>>()`, or any closing
  in another assembly.
- **C: a generated generic registration helper per generic model, closed at the call site.**

  ```csharp
  namespace Ns;

  /// <summary>Registers the generated validators of the generic models in this namespace.</summary>
  public static class ZeroAllocGenericValidatorRegistrationExtensions
  {
      /// <summary>Registers the validator of <c>Page</c> closed over the given type arguments, and every validator it takes.</summary>
      /// <typeparam name="TItem">The <c>TItem</c> type argument of <c>Page</c>.</typeparam>
      /// <param name="services">The service collection to add the registrations to.</param>
      /// <returns>The same service collection, so calls can be chained.</returns>
      public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddPageValidator<TItem>(
          this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
          where TItem : class
      {
          services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Page<TItem>>, global::Ns.PageValidator<TItem>>();
          services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Line<TItem>>, global::Ns.LineValidator<TItem>>();
          return services;
      }
  }

  // Application:
  services.AddZeroAllocValidators().AddPageValidator<Customer>();
  ```

  It is complete and explicit, and works across assemblies. It needs one call per root closing.
- **D: a different service shape,** such as registering `PageValidator<>` open as itself behind a
  non-generic `IValidatorProvider` that closes it with `MakeGenericType`. That is `[RequiresDynamicCode]`,
  so it raises IL3050 in the consumer's AOT publish, and it hits `AotCannotCreateGenericValueType`
  for a value-type closing. Rejected.
- **E: a factory.** MS DI has no open-generic factory, so it cannot be expressed. Rejected.
- **F: discover closings from every `ValidatorFor<X>` constructor parameter in the compilation,**
  the ZeroAlloc.Inject ZAI018 approach. That means a syntax scan of every generic name, and it still
  misses `GetRequiredService` calls and other assemblies. Rejected as the only mechanism. B already
  covers the constructor-parameter closings that matter here, the composed validators.

**Recommendation: B and C together.**

- **B** runs inside the existing `AppendRegistrations`, so `AddZeroAllocValidators()`,
  `ValidateWithZeroAlloc()` and `AddZeroAllocAspNetCoreValidation()` all get it. Composition then
  needs no user code. A node whose type still contains a type parameter, such as
  `Line<TItem>` reached from the open `Page<TItem>`, gets no line in the non-generic method. It is
  registered only from C, closed.
- **C** covers root closings. The helper goes in a static class **in the model's namespace**,
  `ZeroAllocGenericValidatorRegistrationExtensions`, split into a public class and an internal
  `InternalZeroAllocGenericValidatorRegistrationExtensions` by model accessibility and
  `ZeroAllocGeneratedAccessibility`, the way Options splits them. It is named
  `Add{ValidatorName}`, such as `AddPageValidator<TItem>` or `AddEnvelope_HeaderValidator<T>`.
  Validator names are unique per namespace and arity after D-1's ZV0031 fix, so helper names cannot
  collide. Overloads by arity are legal. A single global class could not be used: `A.Box<T>` and
  `B.Box<T>` would both produce `AddBoxValidator<T>` with the same signature, which is CS0111.
- The class name, the method name pattern and the choice of namespace are **public API, and the
  maintainer's call**.

Changes to the existing code:

- `ValidatorDependencies.ValidatedModel` returns `nested.OriginalDefinition`, and
  `RuleEmitter.ValidatorForParameterType` writes `model.OriginalDefinition`. For `Page<Order>` both
  would produce `Page<TItem>`. They must keep the constructed type. `HasGeneratedValidator` keeps
  reading attributes, which a constructed type shares with its definition.
- `RegistrationGraph` is seeded with the closed type. `MemberWalker` then yields the substituted
  property types, `Line<Order>` rather than `Line<TItem>`. `Key` already uses the constructed name.
- The graph gets an expansion guard, see D-7.

**NativeAOT:** `TryAddSingleton<ValidatorFor<Page<int>>, PageValidator<int>>()` is the same call
shape as today. It is statically visible, it roots the value-type instantiation, and its
`[DynamicallyAccessedMembers]` keeps the constructor. B and C are both safe. A, D and E are not.
With the lifetime attribute of D-1, ZeroAlloc.Inject also registers the open `PageValidator<>` as
itself. That path is only a hazard if the application resolves `PageValidator<int>` by its own type
under AOT. The docs say to resolve `ValidatorFor<Page<int>>` instead.

---

### D-5: Options overloads

`ValidateWithZeroAlloc()` has one overload per model on `OptionsBuilder<TModel>`.

**Option A, recommended: one generic overload per generic model, constrained like the model.**

```csharp
/// <summary>Validates <c>Page</c> with its generated validator whenever the options instance is resolved.</summary>
/// <typeparam name="TItem">The <c>TItem</c> type argument of <c>Page</c>.</typeparam>
/// <param name="builder">The options builder to attach validation to.</param>
/// <returns>The same options builder, so calls can be chained.</returns>
public static global::Microsoft.Extensions.Options.OptionsBuilder<global::Ns.Page<TItem>> ValidateWithZeroAlloc<TItem>(
    this global::Microsoft.Extensions.Options.OptionsBuilder<global::Ns.Page<TItem>> builder)
    where TItem : class
{
    var services = builder.Services;
    services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Page<TItem>>, global::Ns.PageValidator<TItem>>();
    services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Line<TItem>>, global::Ns.LineValidator<TItem>>();
    builder.Services.TryAddSingleton<global::Microsoft.Extensions.Options.IValidateOptions<global::Ns.Page<TItem>>,
        global::ZeroAlloc.Validation.Options.ZeroAllocOptionsValidator<global::Ns.Page<TItem>>>();
    return builder;
}

// Application: inference closes TItem.
services.AddOptions<Page<Product>>().BindConfiguration("Catalog").ValidateWithZeroAlloc();
```

- The overload coexists with the non-generic ones and with other generic models' overloads: the
  parameter types differ, and inference picks the right one. The probe confirms both.
- It uses the same registration lines as D-4's helper, closed over the method's type parameters.
  Options does not depend on the Inject package, so it emits them itself through the shared
  `ValidatorRegistrationEmitter`, as it does today.
- The existing record-struct exclusion stays, because `OptionsBuilder<T>` requires `T : class`.
- It is placed in the existing public or internal class, by the same rule as today.

**Option B: per-closing overloads for the closings the compilation uses,** such as
`ValidateWithZeroAlloc(this OptionsBuilder<Page<Product>>)`. That needs a scan for `AddOptions<X>`
calls. It misses closings built in another assembly or through `Configure<T>`, so a closing it
misses gets no overload and the call does not compile. Rejected.

**NativeAOT:** Both are safe. A's call site closes the generic method, so the compiler sees each
instantiation.

---

### D-6: ASP.NET Core filter dispatch

The generated `DispatchAsync(object? arg)` is a `switch` with one `case global::Ns.Model m:` per
model. An open generic cannot be a `case`. The action argument's runtime type is a closing that the
filter's generator usually cannot see, such as a controller parameter `Page<Customer>`.

**Options:**

- **A: one `case` per closing discovered at compile time.** The closings would come from the
  registration graph, plus a scan of controller action parameters. The scan depends on MVC
  conventions: `ControllerBase`, `[Controller]`, the `Controller` suffix, Razor Pages handlers and
  application parts in other assemblies. A missed closing would pass **unvalidated**, which is a
  silent validation bypass. `ValidatedModelInfo.Name` also produces the same variable name,
  `page_arg`, for every closing. Rejected.
- **B: reflection fallback,**
  `GetService(typeof(ValidatorFor<>).MakeGenericType(arg.GetType()))` and a reflective
  `ValidateAsync` call. It raises IL3050 in the consumer's AOT publish, which breaks
  `TreatWarningsAsErrors`, and it hits `AotCannotCreateGenericValueType`. Rejected.
- **C: dispatch through the model,** where each generic model is required to be `partial` and gets
  a generated `ValidateWith(IServiceProvider)`. This adds members to user types and a new
  requirement that non-generic models do not have. It still needs the closed registration. Rejected.
- **D: a registry of closed validators, filled by the registrations, and failing loudly on a miss.**
  Recommended.

**Option D in detail.**

1. **Core gains a non-generic view of a validator.** This is new public API in `ZeroAlloc.Validation`,
   and the name is the maintainer's call:

   ```csharp
   public interface IModelValidator
   {
       Type ModelType { get; }
       ValueTask<ValidationResult> ValidateAsync(object instance, CancellationToken ct);
   }

   public abstract partial class ValidatorFor<T> : IModelValidator
   {
       Type IModelValidator.ModelType => typeof(T);
       ValueTask<ValidationResult> IModelValidator.ValidateAsync(object instance, CancellationToken ct)
           => ValidateAsync((T)instance, ct);
   }
   ```

   The implementation is explicit, in the base class, so no generated or hand-written validator
   changes. Adding an interface to a class passes api-compat. The method has no optional parameter.
2. **Every closed registration of a generic closing also adds a registry entry,** in B and C of D-4,
   in D-5, and in the filter's own `AddZeroAllocAspNetCoreValidation()`:

   ```csharp
   services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<
       global::ZeroAlloc.Validation.IModelValidator,
       global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Page<TItem>>>(
       static sp => sp.GetRequiredService<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Page<TItem>>>()));
   ```

   `TryAddEnumerable` removes duplicates by implementation type, here `ValidatorFor<Page<X>>`, so each
   closing is added once, however many helpers register it. The entry resolves the service rather
   than constructing `PageValidator<X>`, so a registration the application made first still wins.
   Non-generic models get no entry, so their registrations stay byte-identical.
3. **The filter's `default` arm looks the argument's type up** in a generated internal singleton,
   built once. The filter itself is transient, so the lookup table cannot live in it.

   ```csharp
   switch (arg)
   {
       case global::Ns.OrderPage orderPage_arg:
           return await _services.GetRequiredService<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.OrderPage>>().ValidateAsync(orderPage_arg);
       case null:
           return null;
       default:
           return await _generic.ValidateAsync(arg);
   }

   internal sealed class ZeroAllocGenericModelDispatch
   {
       private static readonly global::System.Type[] GenericModels = [typeof(global::Ns.Page<>), typeof(global::Ns.Line<>)];
       private readonly global::System.Collections.Frozen.FrozenDictionary<global::System.Type, global::ZeroAlloc.Validation.IModelValidator> _byType;

       public ZeroAllocGenericModelDispatch(global::System.Collections.Generic.IEnumerable<global::ZeroAlloc.Validation.IModelValidator> validators)
           => _byType = global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(validators, static v => v.ModelType);

       public async global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult?> ValidateAsync(object arg)
       {
           var type = arg.GetType();
           if (_byType.TryGetValue(type, out var validator))
               return await validator.ValidateAsync(arg, default);
           if (type.IsConstructedGenericType && global::System.Array.IndexOf(GenericModels, type.GetGenericTypeDefinition()) >= 0)
               throw new global::System.InvalidOperationException(
                   $"No validator is registered for '{type}', a closing of a generic [Validate] model; " +
                   "register it with the generated Add…Validator<…>() helper.");
           return null;
       }
   }
   ```

   The fail-loud arm covers generic models of this compilation, which are the only models the
   filter dispatches today. A registered closing of a referenced assembly's generic model is
   validated as well, through the registry.

The trade-offs of D:

- An argument that no `case` matches now costs one `GetType()` and one frozen-dictionary lookup,
  with no allocation. `docs/aspnetcore.md` says "no reflection and no dictionary lookup at runtime",
  and needs a correction: the lookup exists, but only for arguments that are not non-generic
  `[Validate]` models.
- A model struct is boxed by MVC before the filter sees it. `(T)instance` unboxes it and does not
  allocate.
- **Needs a decision:** whether to emit the `default` arm and the dispatch class **always**, or
  only when the compilation declares a generic `[Validate]` model. The recommendation is **always**.
  Otherwise an application that only uses a referenced library's generic models, and has none of
  its own, would pass those arguments unvalidated. The cost is a changed filter snapshot for every
  consumer. The dispatch class then has an empty `GenericModels`.

**NativeAOT:** D is safe. The registry delegate is a `static` lambda closed in generic code at a
call site the compiler sees, so no `MakeGenericType` is needed. `Type.GetGenericTypeDefinition` and
`IsConstructedGenericType` are not annotated for dynamic code or trimming. `FrozenDictionary` is
AOT-safe. A is safe but incomplete. B is not safe. C is safe.

---

### D-7: nested composition and accessibility

**Recommendation: compose by the statically known `[Validate]` type, and nothing else.**

| Property | Composed? | Constructor parameter |
|---|---|---|
| `Line<TItem> Line` in `Page<TItem>` | yes | `ValidatorFor<global::Ns.Line<TItem>>` |
| `List<Line<TItem>> Lines` | yes, element by element | `ValidatorFor<global::Ns.Line<TItem>>` |
| `Page<Order> Page` in non-generic `OrderPage` | yes | `ValidatorFor<global::Ns.Page<global::Ns.Order>>`, registered by D-4 B |
| `TItem Item`, `List<TItem> Items`, with `TItem` unconstrained or constrained to a non-`[Validate]` type | **no**, rules only | none |
| `TItem Item` with `where TItem : Address`, a `[Validate]` class | yes, as `Address` | `ValidatorFor<global::Ns.Address>`, the same as for a property declared `Address`, which holds a subclass today |

Rejected alternative for a bare `TItem`: an optional `ValidatorFor<TItem>? itemValidator = null`
constructor parameter, used when the container has one. Whether the property is validated would
then depend on what else happens to be registered. `ValidatorFor<int>` is never registered, so the
parameter is dead for every value-type closing. It would also add a constructor parameter that the
registration graph cannot follow.

Changes to the code:

- **Constructed types throughout:** `ValidatorDependencies.ValidatedModel`,
  `RuleEmitter.ValidatorForParameterType` and `RegistrationGraph` keep the constructed type, see D-4.
  `NestedValidateCandidates` and `CollectionValidateCandidates` accept a type parameter through its
  `[Validate]` class constraint.
- **Accessibility:** `NestedValidatorAccessibility.IsEffectivelyPublic` already treats a type
  parameter as public and walks type arguments. `PageValidator<TItem>` is public exactly when
  `Page<TItem>` and `Line<TItem>` are. `OrderPageValidator` becomes internal when it takes
  `ValidatorFor<Page<InternalOrder>>`. No change is needed, only tests.
- **Expanding recursion:** `class Node<T> { public Node<Node<T>>? Next { get; set; } }` compiles,
  as the probe confirms. Seeding the registration graph with `Node<int>` would then walk
  `Node<Node<int>>`, `Node<Node<Node<int>>>` and so on, and `seen` never stops it, **so the
  generator hangs**. A property whose type contains, as a type argument, a closing of a generic
  definition already on the path reports **new ZV0037**, Error, at the property: "'{0}' nests '{1}'
  inside itself, so its validators would form an unbounded chain; validate it with [ValidateWith] or
  a [Must] rule". It is not composed. Plain self-reference, `Node<T> { Node<T>? Next }`, reaches
  `Node<T>` again and stops as today. It behaves exactly as a self-referencing non-generic model does.
- **Probes:** `MethodCallProbe.ValidatedTypes` drops its `!type.IsGenericType` filter.
  `ProbeText` emits `Call0<TItem>(global::Ns.Page<TItem> instance) where TItem : class`, and
  `RuleEmitter.EmitWarningProbe` emits `internal sealed class Probe<TItem> where TItem : class`,
  with the same type parameters and constraints as the validator. Without this, the CS warning
  mirroring of ZV0028 and ZV0030 and the obsolete-error checks never run for generic models.
- **Base types:** a non-generic `OrderPage : Page<Order>` still validates the inherited members,
  with substituted types, as today. `MethodReachability.FindReportingBaseValidator` now finds a
  generated validator for `Page<Order>`, so the inherited members' diagnostics are reported once, by
  `Page<TItem>`'s generation. The ZV0029 docs, "reports their diagnostics itself", change with it.

**NativeAOT:** safe. Composition is constructor injection of closed `ValidatorFor<X>` types, as
today.

---

### D-8: diagnostics

The IDs are allocated here, up front, because two other in-flight changes already claim the next
ones: #202 has ZV0034 and #293 has ZV0035.

| ID | Severity | Change |
|---|---|---|
| ZV0029 | Error | **Narrowed** to a repeated type parameter name along the containing chain, or one named like the validator, see D-1. It goes under "Changed Rules" in `AnalyzerReleases.Unshipped.md`. |
| ZV0033 | Error | **Extended** to a type-parameter operand without an `INumberBase<T>` constraint. The message hint is updated. |
| ZV0036 | Error | **New:** a built-in rule has no form for a type-parameter operand, see D-2. |
| ZV0037 | Error | **New:** expanding generic recursion in nested composition, see D-7. |
| ZV0038 | Warning | **New:** a behaviour's `AppliesTo` names a closed form of a generic `[Validate]` model, see D-3. It is a Warning, because the same code compiles, and runs no behaviour, today. |

## NativeAOT summary

| Decision | Recommended option | AOT |
|---|---|---|
| D-1 shape | generic validator | safe; value-type closings rooted by closed registrations or `new` |
| D-2 rules on `T` | constraint-based forms, ZV0033 and ZV0036 otherwise | safe, zero-alloc: `CreateChecked`, `Enum.IsDefined<T>` |
| D-3 behaviours | open `AppliesTo` match, ZV0038 | safe, static calls |
| D-4 DI | closed graph registrations and a generic helper | safe; no open-generic descriptor, no `MakeGenericType` |
| D-5 Options | generic overload | safe |
| D-6 filter | registry and fail-loud | safe; `GetGenericTypeDefinition` only |
| D-7 composition | constructed types, constraint composition, ZV0037 | safe |

The rejected options that are **not** AOT-safe: the open-generic `ServiceDescriptor` of D-4 A, the
`MakeGenericType` provider of D-4 D, and the reflection fallback of D-6 B.

## Phased implementation

Each phase is one PR with a `feat(generator):` commit. Each phase only lifts an error or adds API,
so it is a **minor** release. The release hold may batch them.

1. **Phase 1: validator and composition.** D-1, D-2, D-3, D-7 and D-8, plus D-4 B in the shared
   `ValidatorRegistrationEmitter`, because a non-generic model's validator that takes
   `ValidatorFor<Page<Order>>` must be registered in the same release, or the container cannot build
   it. The companion generators never list an open generic model as a root of their non-generic
   methods, in this or any phase, because nothing closed can be registered for it: their transforms
   read `HasGeneratedValidator && !IsGeneric` for the root list. Its closings arrive through B, and
   in phase 2 also through the helper and the overload. ZV0029 is narrowed.
2. **Phase 2: Inject helper and Options.** D-4 C and D-5. This adds generated public API in the
   consumer's assembly, and no API in this repo's packages.
3. **Phase 3: ASP.NET Core dispatch.** D-6 with `IModelValidator` in core. Its
   `PublicAPI.Unshipped.txt` entries, and the registry entries emitted by phases 1 and 2's
   registrations, ship here. Phase 3 changes the output of phases 1 and 2, adding the
   `TryAddEnumerable` lines. The alternative is to ship `IModelValidator` in phase 1 and add the
   entries from the start, which avoids changing emitted code twice. **Needs a decision:** that
   pulls the core API decision forward.

Behaviour changes for each phase's PR body, under "Behaviour change":

- **Phase 1:** a project that set ZV0029's severity to `none` through `.editorconfig` built with
  generic `[Validate]` models and no validators. For that project, after the upgrade:
  - those models get validators;
  - a property of a closed generic `[Validate]` type in a non-generic model is now validated as a
    nested model;
  - that non-generic model's validator constructor gains a `ValidatorFor<…>` parameter, which
    breaks code that constructs the validator by hand.

  Every other project failed to build on ZV0029, so nothing changes for it.
- **Phase 3:** the filter's `default` arm now validates registered closings of generic models, and
  throws for an unregistered closing of one of this compilation's generic models, instead of letting
  it through.

## Test plan

**Generator snapshots and diagnostics**, as new `GenericValidateModelTests` beside the existing
`GenericValidateTypeDiagnosticTests`, which shrinks to the narrowed ZV0029:

- Shape:
  - one, two and three type parameters;
  - every constraint kind: `class`, `class?`, `struct`, `unmanaged`, `notnull`, `new()`, interface
    and base-class constraints, a constraint naming another type parameter, and `allows ref struct`;
  - a model in a generic container, and a generic model in a generic container;
  - `record`, `readonly struct` and `readonly record struct`;
  - `<typeparam>` tags, with no CS1570 or CS1712 under `GenerateDocumentationFile`.
- Names:
  - hint and metadata names with the arity suffix;
  - `Box` beside `Box<T>` produces two validators and no ZV0031;
  - `Outer<T>.Inner` beside a top-level `Outer_Inner<T>` still reports ZV0031;
  - `Outer<T>.Inner<T>` reports the narrowed ZV0029.
- One case per row of the D-2 table:
  - ZV0033 for unconstrained `T` and for `T : IConvertible`;
  - `CreateChecked` for `T : INumber<T>`, on `T` and on `T?`;
  - ZV0036 for each listed rule;
  - `Enum.IsDefined<T>`;
  - `[Must]`, and a custom rule to `object`, to an interface and to `int`, the last reporting ZV0021;
  - the null-guard cases for a possibly-null `T`;
  - `{PropertyValue}` on `T`.
- Behaviours: `typeof(Page<>)`, `typeof(Outer<>.Inner<>)` and `typeof(Outer<>.Plain)` apply.
  `typeof(Page<Order>)` reports ZV0038 at the attribute and does not apply.
- Composition: every row of the D-7 table, and ZV0037 for `Node<Node<T>>`. The ZV0037 test carries a
  timeout, because the failure mode is a hang.
- Probes: a ZV0028, a mirrored CS warning and a ZV0032 on a generic model are each reported once, at
  the attribute.
- Base types: a diagnostic on a member of generic `[Validate]` `Page<T>` is reported once when
  `OrderPage : Page<Order>` is also `[Validate]`.
- `IncrementalCachingTests` for all three companion generators with a generic model present.
- **Existing snapshots stay byte-identical** in phases 1 and 2. In phase 3, only the filter's
  `default` arm changes.

**Runtime tests:**

- A generic model closed over a value type, a reference type and a nullable reference type
  validates correctly: every supported rule, nested and collection composition, and behaviours.
- Inject:
  - `AddZeroAllocValidators()` resolves `OrderPage` with its `Page<Order>` and `Line<Order>` graph;
  - `AddPageValidator<Customer>()` resolves a root closing;
  - `GetService<ValidatorFor<Page<Unregistered>>>()` returns **null**, which guards
    ZeroAlloc.Mediator's contract;
  - a registration the application made first wins.
- Options: `AddOptions<Page<Product>>().ValidateWithZeroAlloc()` fails startup validation on an
  invalid section.
- ASP.NET Core:
  - an action argument `Page<Customer>` registered through the helper returns 422 when invalid;
  - an unregistered closing throws the documented `InvalidOperationException`;
  - a non-generic model behaves as before.
- `AllocationRegressionTests`: the valid path of `Page<int>`-style value-type and reference-type
  closings, including an `INumber<T>` comparison, is pinned at zero bytes.

**AOT smoke** in `samples/ZeroAlloc.Validation.AotSmoke`: a generic model closed over a value type
and over a class, one nested closing and one `CreateChecked` comparison. Phase 3 adds the
dispatch registry, if the sample hosts MVC; otherwise a direct `IModelValidator` call. The
value-type closing is required, because it is the case the open-generic paths fail on.

**PackSmoke:** a consumer declares a generic model against the packed packages, registers a closing
through the helper and validates it.

**api-compat:** `IModelValidator` and `ValidatorFor<T> : IModelValidator` in phase 3, with the
`PublicAPI.Unshipped.txt` entries.

## Docs, per phase

- `docs/getting-started.md`: remove the ZV0029 paragraph and describe generic models.
- `docs/diagnostics.md`: the narrowed ZV0029, the extended ZV0033, and ZV0036, ZV0037 and ZV0038.
  ZV0011 and ZV0031 mention ZV0029, and change with it.
- `docs/nested-validation.md` and `docs/collection-validation.md`: the D-7 table.
- `docs/custom-validation.md`: rules on a type parameter, and boxing for a value-type closing.
- `docs/inject.md`: closed graph registrations and the `Add…Validator<…>()` helper, and the
  ZeroAlloc.Inject lifetime-attribute caveat.
- `docs/options.md`: the generic overload.
- `docs/aspnetcore.md`: the registry, the fail-loud arm, and the corrected "no dictionary lookup"
  sentence.

## Decisions for the maintainer

1. **D-1:** generic validators with declared type parameter names, and ZV0029 narrowed to repeated
   names, rather than renaming type parameters.
2. **D-2:** reject rules without a constraint-based form with ZV0036. Numeric rules require
   `INumberBase<T>`, so `T : IConvertible` is rejected rather than boxed.
3. **D-4:** the helper's class name, method name pattern and namespace placement,
   `Ns.ZeroAllocGenericValidatorRegistrationExtensions.AddPageValidator<TItem>()`.
4. **D-6:** the new core API `IModelValidator` and its name. Whether the filter's `default` arm is
   emitted always, as recommended, or only with local generic models.
5. **Phasing:** whether `IModelValidator` ships in phase 1, so the registrations are emitted once in
   their final form, or in phase 3.
6. **D-8:** the diagnostic IDs ZV0036 to ZV0038, and ZV0038 as a Warning.

## Out of scope

- Constraint-based forms for the ZV0036 rules, D-2 Option C. Each one later is a minor that lifts
  an error.
- Unbound `[ValidateWith(typeof(X<>))]` closed over the model's type parameters. Today an unbound
  type is not constructible and is not registered, and that stays as it is.
- Validation through a runtime-resolved `ValidatorFor<T>` for a bare type parameter, see D-7.
