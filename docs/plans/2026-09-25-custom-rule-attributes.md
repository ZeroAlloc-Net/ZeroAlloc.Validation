# Custom Rule Attributes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users write reusable property rule attributes by deriving from `ValidationAttribute<T>`, emitted by the generator as a statically bound `IsValid` call, and make a subclass the generator cannot emit a build error instead of silently ignoring it.

**Architecture:**
- **Recognition:** `RuleEmitter` recognises any attribute deriving from `ValidationAttribute<T>`.
- **Emission:** each usage is rebuilt as a `private static readonly` field on the generated validator. It is collected through the same per-class dictionary mechanism `[Matches]` uses for its regex fields. The condition is `!field.IsValid(rawAccess)`.
- **Messages:** default messages come from a class-level `[RuleMessage]`, with placeholders resolved at compile time.
- **Diagnostics:** three new IDs, ZV0020 to ZV0022.

**Tech Stack:** C# Roslyn incremental source generator targeting netstandard2.0; runtime library net8.0, net9.0 and net10.0; xUnit.

**Spec:** `docs/plans/2026-09-25-custom-rule-attributes-design.md`. Read it first; it is the authority.

## Global Constraints

- No reflection, `Activator` or `Type.GetType` in generated code or on the validation path; must stay trim- and NativeAOT-safe.
- Nothing is allocated per validation call. One instance per custom-rule usage is created when the validator type initialises.
- Existing generated output for built-in rules stays byte-identical. Existing tests must pass unchanged.
- Diagnostic IDs: ZV0020 Error, ZV0021 Error, ZV0022 Warning, ZV0023 Error, category `ZeroAlloc.Validation`, each registered in `src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md`.
- `ValidationAttribute<T>` has exactly one member of its own: `public abstract bool IsValid(T value);`. Do not add async or virtual members; async rules are tracked in #202.
- Base members `Message`, `When`, `Unless`, `ErrorCode` and `Severity` are never copied into the rebuilt instance.
- Final delivery is a single commit, `feat!: support user-defined rule attributes via ValidationAttribute<T>`, with a `BREAKING CHANGE:` footer for ZV0020 and `Closes #196`.
  - Body lines are at most 100 characters, with no nested parentheses anywhere in the message and no claude.ai session URL.
  - It ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Build or test commands run from the repo root `C:\wt\validation-gen-access`. Use `-c Release`.

## File Structure

| File | Responsibility |
|---|---|
| `src/ZeroAlloc.Validation/Attributes/ValidationAttributeOfT.cs` (new) | `ValidationAttribute<T>` |
| `src/ZeroAlloc.Validation/Attributes/RuleMessageAttribute.cs` (new) | `RuleMessageAttribute` |
| `src/ZeroAlloc.Validation.Generator/CustomRules.cs` (new) | All custom-rule symbol logic: detecting `ValidationAttribute<T>`, getting `T`, building the instance initializer, finding `[RuleMessage]`, resolving named placeholders |
| `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs` | Wire custom rules into `IsRuleAttribute`, `BuildCondition`, message and ErrorCode resolution, and the diagnostics |
| `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs` | Emit the collected rule-instance fields next to the regex fields |
| `tests/ZeroAlloc.Validation.Tests/Generator/CustomRuleAttributeTests.cs` (new) | Generator emission and diagnostic tests |
| `tests/ZeroAlloc.Validation.Tests/Integration/CustomRuleAttributeTests.cs` (new) plus model files | Runtime behaviour |
| `tests/ZeroAlloc.Validation.PackSmoke/CustomRulePackTests.cs` (new) | Packed-package consumer |
| `docs/custom-validation.md`, `docs/diagnostics.md`, `docs/migrating-to-v2.md` | Documentation |

---

### Task 1: Public API types

**Files:**
- Create: `src/ZeroAlloc.Validation/Attributes/ValidationAttributeOfT.cs`
- Create: `src/ZeroAlloc.Validation/Attributes/RuleMessageAttribute.cs`
- Modify: the package's public-API tracking, if the repo has any. Run `git grep -l "PublicAPI" -- src/ZeroAlloc.Validation` and the api-compat setup in `Directory.Build.props` and `.github/workflows`, then follow what exists.
- Test: `tests/ZeroAlloc.Validation.Tests/Attributes/AttributeDeclarationTests.cs`

**Interfaces:**
- Produces: `ZeroAlloc.Validation.ValidationAttribute<T>` with `public abstract bool IsValid(T value)`, and `ZeroAlloc.Validation.RuleMessageAttribute(string message)` with `string Message { get; }` and `string? ErrorCode { get; set; }`.

- [ ] **Step 1: Write failing tests.** Add them to `AttributeDeclarationTests.cs`, following the file's existing style:

```csharp
[Fact]
public void ValidationAttributeOfT_is_abstract_property_attribute_deriving_from_ValidationAttribute()
{
    var t = typeof(ValidationAttribute<>);
    Assert.True(t.IsAbstract);
    Assert.Equal(typeof(ValidationAttribute), t.BaseType);
    var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(t, typeof(AttributeUsageAttribute))!;
    Assert.Equal(AttributeTargets.Property, usage.ValidOn);
    Assert.True(usage.AllowMultiple);
    var declared = t.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
    var only = Assert.Single(declared, m => m.MemberType == System.Reflection.MemberTypes.Method);
    Assert.Equal("IsValid", only.Name);
}

[Fact]
public void RuleMessageAttribute_targets_classes_and_carries_message_and_error_code()
{
    var a = new RuleMessageAttribute("{PropertyName} bad.") { ErrorCode = "BAD" };
    Assert.Equal("{PropertyName} bad.", a.Message);
    Assert.Equal("BAD", a.ErrorCode);
    var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(RuleMessageAttribute), typeof(AttributeUsageAttribute))!;
    Assert.Equal(AttributeTargets.Class, usage.ValidOn);
    Assert.False(usage.AllowMultiple);
    Assert.True(usage.Inherited);
}
```

The `DeclaredOnly` public-instance member check allows exactly one method, `IsValid`. The protected constructor is not public, so it is excluded.

- [ ] **Step 2: Run the tests; expect a compile failure.** Run `dotnet test tests/ZeroAlloc.Validation.Tests -c Release --filter "FullyQualifiedName~AttributeDeclarationTests"`. The expected failure is CS0246 or CS0305.

- [ ] **Step 3: Implement.**

```csharp
// ValidationAttributeOfT.cs
namespace ZeroAlloc.Validation;

/// <summary>
/// Base class for a reusable, user-defined property rule. The source generator rebuilds each
/// usage once as a static instance of the derived attribute and calls <see cref="IsValid"/>
/// with the property value. No reflection is involved, and nothing is allocated per validation.
/// </summary>
/// <typeparam name="T">The value type the rule checks. The property type must convert to it implicitly.</typeparam>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class ValidationAttribute<T> : ValidationAttribute
{
    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> passes the rule.</summary>
    public abstract bool IsValid(T value);
}
```

```csharp
// RuleMessageAttribute.cs
namespace ZeroAlloc.Validation;

/// <summary>
/// Declares the default failure message, and optionally the error code, for a custom rule
/// attribute deriving from <see cref="ValidationAttribute{T}"/>. A <c>Message</c> or
/// <c>ErrorCode</c> set on the usage wins. Placeholders are resolved at compile time:
/// <c>{PropertyName}</c>, <c>{PropertyValue}</c>, and <c>{name}</c> for any constructor
/// parameter or named property written on the usage.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RuleMessageAttribute(string message) : Attribute
{
    public string Message { get; } = message;
    public string? ErrorCode { get; set; }
}
```

  Also update the API tracking found in the Files step.

- [ ] **Step 4: Run the tests; expect them to pass.** Run the same command as Step 2, then the full `dotnet build -c Release` with 0 warnings.

- [ ] **Step 5: Commit.** `git commit -m "feat: add ValidationAttribute<T> and RuleMessageAttribute"`

---

### Task 2: Recognise and emit custom rules

**Files:**
- Create: `src/ZeroAlloc.Validation.Generator/CustomRules.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
  - `IsRuleAttribute` at about line 40
  - `BuildCondition` at about line 841
  - both emit loops, at about lines 346 and 574
  - `GetUnreachableConditionMethods` at about line 664
  - the `regexMethods` parameter plumbing
- Modify: `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs`, at about lines 204–245: field emission
- Test: `tests/ZeroAlloc.Validation.Tests/Generator/CustomRuleAttributeTests.cs`

**Interfaces:**
- Consumes: Task 1 types, by metadata name: `ZeroAlloc.Validation.ValidationAttribute`1` and `ZeroAlloc.Validation.RuleMessageAttribute`.
- Produces, in `internal static class CustomRules`:
  - `static bool TryGetRuleValueType(INamedTypeSymbol attrClass, out ITypeSymbol valueType)`: walks `BaseType` and returns `T` of the first closed `ZeroAlloc.Validation.ValidationAttribute<T>`.
  - `static string BuildInitializer(AttributeData attr)`: returns, for example, `new global::Ns.MinWordsAttribute(3) { Tag = "x" }`.
  - `static string FieldName(string propName, int ruleIndex)`: returns `__Rule_{propName}_{ruleIndex}`.

**Field collection.** The existing `Dictionary<string,string> regexMethods` is threaded through the emit paths and deduplicates the sync and async double visit. Replace that parameter's type everywhere with a new `internal sealed class GeneratedFields` in `CustomRules.cs`:

```csharp
internal sealed class GeneratedFields
{
    public Dictionary<string, string> RegexPatterns { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (string TypeName, string Initializer)> RuleInstances { get; } = new(StringComparer.Ordinal);
}
```

- `BuildMatchesCondition` writes to `RegexPatterns`, which behaves as today.
- `EmitMatchesRegexFields` reads `RegexPatterns`.
- A new `EmitRuleInstanceFields` emits one field for each `RuleInstances` entry:

```csharp
    private static readonly global::Ns.MinWordsAttribute __Rule_Title_0
        = new global::Ns.MinWordsAttribute(3);
```

- Emit the regex fields first, then the rule fields, so built-in output stays byte-identical.
- Keep the parameter name `regexMethods` or rename it to `fields`, whichever keeps the diff smaller. The rename is optional.

**Field naming.** `ruleIndex` is the index of the attribute within that property's `rules` list, the same `i` as in the emit loops. The name is therefore identical on the sync and async visits, and the dictionary deduplicates it.

**Initializer rules.** These are implemented in `BuildInitializer`:
- **Type name:** `attr.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)`, which also handles closed generics.
- **Constructor arguments:** `attr.ConstructorArguments`, each rendered with `TypedConstant.ToCSharpString()`. One exception: for `TypedConstantKind.Type`, use `typeof(` plus the fully qualified type name plus `)`, because `ToCSharpString` output is not guaranteed to be fully qualified.
  - A null array renders as `null`.
  - An array renders as `new global::ElemType[] { a, b }`, recursing element by element.
  - An enum renders as `(global::EnumType)value`, cast from the underlying constant, so flags values also work.
- **Named arguments:** `attr.NamedArguments`, excluding `Message`, `When`, `Unless`, `ErrorCode` and `Severity`, rendered as `{ Name = value, ... }`. Omit the braces when none are left.

**Condition.** In `BuildCondition`, check `CustomRules.TryGetRuleValueType(attr.AttributeClass, out _)` before the `fqn switch`. When it matches:
- register `(TypeName, Initializer)` under `FieldName(propName, ruleIndex)` in `RuleInstances`
- return `$"!{field}.IsValid({rawForPredicate})"`

`BuildCondition` needs `ruleIndex`, so add it as a parameter. It is private, so the signature can change freely.

**Message fallback.** For now, a custom rule falls back to `"{propName} is invalid."` in `GetDefaultMessage`, the same as `[Must]`. Task 3 adds `[RuleMessage]`.

**Recognition.** `IsRuleAttribute` returns true for the existing names, or when `CustomRules.TryGetRuleValueType` succeeds. Every existing caller then includes custom rules, which the spec requires.

- [ ] **Step 1: Write failing generator tests.** Create `CustomRuleAttributeTests.cs`.
  - Build a `RunGenerator(string source)` helper modelled on the one in `ValueObjectPropertyDiagnosticTests.cs`: same references and the same `GetGeneratedSource`.
  - Add `CompileErrors(GeneratorDriverRunResult result, Compilation output)`. It returns `output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)`, so tests can assert the generated code compiles.
  - Use the driver's `RunGeneratorsAndUpdateCompilation` to get the output compilation.

  Tests:

```csharp
[Fact]
public void NotBlank_without_arguments_emits_static_field_and_IsValid_call()
{
    var source = """
        using ZeroAlloc.Validation;
        namespace TestModels;

        public sealed class NotBlankAttribute : ValidationAttribute<string?>
        {
            public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
        }

        [Validate]
        public sealed class Request
        {
            [NotBlank] public string? Name { get; init; }
        }
        """;

    var (result, output) = RunGenerator(source);

    Assert.Empty(CompileErrors(output));
    var src = GetGeneratedSource(result, "RequestValidator.g.cs");
    Assert.Contains("private static readonly global::TestModels.NotBlankAttribute __Rule_Name_0", src, StringComparison.Ordinal);
    Assert.Contains("= new global::TestModels.NotBlankAttribute();", src, StringComparison.Ordinal);
    Assert.Contains("!__Rule_Name_0.IsValid(instance.Name)", src, StringComparison.Ordinal);
}
```

  Further `[Fact]`s, each asserting zero compile errors plus the specific emitted text:
  - `Constructor_and_named_arguments_of_every_constant_kind_are_rebuilt`
    - An attribute with constructor `(int i, string s, char c, Color e, System.Type t, int[] a, string? n)` and a named `bool Flag { get; set; }`.
    - The usage is `[AllKinds(3, "x\"y", 'q', Color.Red, typeof(System.Guid), new[] { 1, 2 }, null, Flag = true, Message = "m", ErrorCode = "E")]`.
    - Assert the initializer contains `3`, `"x\"y"`, `'q'`, `(global::TestModels.Color)`, `typeof(global::System.Guid)`, `new int[] { 1, 2 }` (or the fully qualified `global::System.Int32` form; assert on whichever form is emitted, but it must compile), `null` and `Flag = true`.
    - Assert it does **not** contain `Message =` or `ErrorCode =`.
  - `Indirect_base_is_recognised`: `abstract class StringRule : ValidationAttribute<string?>` and `sealed class NoDigits : StringRule`.
  - `Closed_generic_attribute_uses_closed_type_name`: `sealed class InRange<T>(T min, T max) : ValidationAttribute<T> where T : System.IComparable<T>`, used as `[InRange<int>(1, 5)]` on an `int` property. The field type is `global::TestModels.InRange<int>`.
  - `When_Unless_Severity_ErrorCode_and_Message_apply_to_custom_rules`: assert the `instance.Cond() &&` guard, the message text and the error code in the failure initializer, as the existing built-in tests do. Find an existing `When` test with `grep -rn "When =" tests/ZeroAlloc.Validation.Tests/Generator`.
  - `StopOnFirstFailure_chains_custom_rule_with_else_if`: `[StopOnFirstFailure]`, `[NotEmpty]` and `[NotBlank]` on one property. Assert `else if (!__Rule_Name_1.IsValid`.
  - `ValueObject_property_passes_wrapper`: copy the value-object stub from `ValueObjectPropertyDiagnosticTests`. The rule is `ValidationAttribute<CustomerId>`. Assert `IsValid(instance.CustomerId)` and no `.Value`.
  - `Unreachable_When_on_custom_rule_reports_ZV0017`: mirror the existing ZV0017 test. Find it with `grep -rn ZV0017 tests`.
  - `Sync_and_async_paths_emit_field_once`: add a model with an async behaviour, or use whatever the existing tests use to force the `ValidateAsync` override; find it with `grep -rn "ValidateAsync" tests/ZeroAlloc.Validation.Tests/Generator`. Assert exactly one occurrence of `private static readonly global::TestModels.NotBlankAttribute`.
  - `Builtin_output_unchanged`: for a model with only `[NotEmpty]` and `[Matches("a+")]`, assert the generated source contains no `__Rule_`.

- [ ] **Step 2: Run the tests; expect them to fail.** Run `dotnet test tests/ZeroAlloc.Validation.Tests -c Release --filter "FullyQualifiedName~Generator.CustomRuleAttributeTests"`. The expected failure is no `__Rule_` in the output, because the attribute is ignored today.

- [ ] **Step 3: Implement `CustomRules.cs`, the `GeneratedFields` plumbing and the `RuleEmitter` changes** described above.

- [ ] **Step 4: Run the tests; expect them to pass.** Run the filter from Step 2, then the whole `tests/ZeroAlloc.Validation.Tests` project. Every pre-existing test must pass unchanged.

- [ ] **Step 5: Add a runtime integration test.**
  - Put the models in `tests/ZeroAlloc.Validation.Tests/Integration/CustomRuleModels.cs`:
    - `NotBlankAttribute`
    - `MinWordsAttribute(int minWords)`, which counts whitespace-separated words and treats null as 0
    - `[Validate] public sealed class CustomRuleModel { [NotBlank] public string? Name { get; set; } [MinWords(3)] public string? Title { get; set; } }`
  - In `Integration/CustomRuleAttributeTests.cs`, use the generated `CustomRuleModelValidator` the same way the other Integration tests do. Assert that:
    - `Name` values `null`, `""` and `"   "` fail, and `"a"` passes
    - `Title` value `"one two"` fails and `"one two three"` passes
    - a valid model produces zero failures
  - Run the Integration filter and expect it to pass.

- [ ] **Step 6: Commit.** `git commit -m "feat(generator): emit user-defined ValidationAttribute<T> rules"`

---

### Task 3: `[RuleMessage]` defaults, named placeholders and ZV0022

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/CustomRules.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
  - `ResolveMessage` at about line 614
  - `GetDefaultMessage` at about line 903
  - error-code resolution inside `BuildFailureInitializer`; locate it with `grep -n "ErrorCode" src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md`
- Test: `tests/ZeroAlloc.Validation.Tests/Generator/CustomRuleAttributeTests.cs`

**Interfaces:**
- Produces, in `CustomRules`:
  - `static (string Message, string? ErrorCode)? FindRuleMessage(INamedTypeSymbol attrClass)`: walks the class and its base types, nearest first, and returns the first `ZeroAlloc.Validation.RuleMessageAttribute`.
  - `static string ResolveNamedPlaceholders(string message, AttributeData attr, out IReadOnlyList<string> unknown)`: substitutes each `{name}`, excluding the reserved `PropertyName` and `PropertyValue`. It looks the name up among the constructor's parameter names first (via `attr.AttributeConstructor.Parameters[i].Name`, matched to `ConstructorArguments[i]`), then among the named arguments on the usage.
    - Values are formatted invariant-culture. Strings are raw, without quotes. Enums use the member name, falling back to the numeric value. `typeof` uses the type's display name. Arrays are joined with `", "`.
    - Matching is case-sensitive.
    - Unmatched names are returned in `unknown`, and their text is left as it is.

**Behaviour, for custom rules only:**
- **Message precedence:** the usage's `Message`, then `FindRuleMessage`, then `"{PropertyName} is invalid."`.
  - `{PropertyName}` behaves as today, becoming the display name.
  - The existing `{PropertyValue}` runtime handling is unchanged.
  - Named placeholders are substituted in messages from either source, the usage or `[RuleMessage]`.
- **ErrorCode precedence:** the usage's `ErrorCode`, then `FindRuleMessage().ErrorCode`, then none.
- **ZV0022:** for each unknown placeholder, report a Warning at the property location, when `ctx` is not null.
  - Message format: `Placeholder '{0}' in the message for '{1}' on '{2}' does not match any argument; it is emitted literally`. The arguments are the placeholder name without braces, the attribute class name and the property name.
  - Report once per usage and name, not twice for the sync and async visits. Only the visit with a non-null `ctx` reports: `EmitValidateBody` receives `ctx`, while the async string path is called without it at `ValidatorGenerator.cs:308`. Confirm this; if both visits pass `ctx`, deduplicate with a `HashSet` keyed on field name plus placeholder.

- [ ] **Step 1: Write failing tests.** Add them to `Generator/CustomRuleAttributeTests.cs`:
  - `RuleMessage_supplies_default_message_and_error_code`: `[RuleMessage("{PropertyName} must not be blank.", ErrorCode = "NOT_BLANK")]` on `NotBlankAttribute`. Assert that the generated failure initializer contains `"Name must not be blank."` and `"NOT_BLANK"`.
  - `Usage_Message_and_ErrorCode_override_RuleMessage`: `[NotBlank(Message = "custom", ErrorCode = "X")]`. Assert `"custom"` and `"X"`, and that neither `must not be blank` nor `NOT_BLANK` appears.
  - `RuleMessage_is_found_on_base_class`: `[RuleMessage("{PropertyName} base msg.")]` on the abstract base, and none on the derived class. Assert `"Name base msg."`.
  - `Named_placeholders_resolve_from_ctor_parameter_and_property`: `[RuleMessage("{PropertyName} needs {minWords} words, {MinWords} min.")] sealed class MinWordsAttribute(int minWords) : ValidationAttribute<string?> { public int MinWords { get; } = minWords; ... }`, used as `[MinWords(3)]`. Assert `"Title needs 3 words, {MinWords} min."`.
    - `MinWords` is a get-only property that is not written on the usage, so it is unknown and stays literal.
    - Also assert that ZV0022 is reported once, for `MinWords`. This pins the spec's "as written on the usage" rule.
  - `Named_placeholder_from_named_argument`: `[Tagged(Tag = "abc")]` with `[RuleMessage("{PropertyName} {Tag}")]`. Assert `"Name abc"`.
  - `Placeholders_in_usage_Message_resolve_too`: `[MinWords(4, Message = "{PropertyName}: {minWords}+")]`. Assert `"Title: 4+"`.
  - `Unknown_placeholder_reports_ZV0022_warning_once`: `[RuleMessage("{PropertyName} {nope}")]`. Assert exactly one ZV0022 with Warning severity, and that the message still contains `{nope}`.
  - `Builtin_messages_unchanged`: a model with `[NotEmpty(Message = "{PropertyName} {minWords}")]`. Assert the output keeps `{minWords}` literally and there is no ZV0022. Named placeholders are for custom rules only.

- [ ] **Step 2: Run the tests; expect them to fail.** Run `dotnet test tests/ZeroAlloc.Validation.Tests -c Release --filter "FullyQualifiedName~Generator.CustomRuleAttributeTests"`.

- [ ] **Step 3: Implement** the helpers and the precedence described above. Add the ZV0022 descriptor next to ZV0016 in `RuleEmitter.cs`, using the same constructor shape. Add a line to `AnalyzerReleases.Unshipped.md`:
```
ZV0022  | ZeroAlloc.Validation | Warning  | Unknown placeholder in a custom rule message
```

- [ ] **Step 4: Run the tests; expect them to pass.** Run the whole `tests/ZeroAlloc.Validation.Tests` project.

- [ ] **Step 5: Commit.** `git commit -m "feat(generator): add RuleMessage defaults and named placeholders for custom rules"`

---

### Task 4: ZV0020, ZV0021 and ZV0023

**Files:**
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`, plus `ValidatorGenerator.cs` if property iteration lives there
- Modify: `src/ZeroAlloc.Validation.Generator/AnalyzerReleases.Unshipped.md`
- Test: `tests/ZeroAlloc.Validation.Tests/Generator/CustomRuleAttributeTests.cs`

**Behaviour:**
- **ZV0020 (Error):** for each property that the generator inspects on a `[Validate]` model, the same properties whose rules are collected today, and each attribute on it:
  - if the attribute class derives from `ZeroAlloc.Validation.ValidationAttribute`, and
  - it is not recognised by `IsRuleAttribute`, and
  - it is not otherwise handled by the generator
  - then report ZV0020 at the attribute's location, `attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()`, falling back to the property location.
  - "Otherwise handled" means an attribute the generator reads elsewhere. Check what derives from `ValidationAttribute` today: run `grep -rn ": ValidationAttribute" src/ZeroAlloc.Validation`. Per the current source, only the built-ins and `MustAttribute` do. `SkipWhen`, `StopOnFirstFailure`, `DisplayName`, `ValidateWith`, `Validate` and `CustomValidation` derive from `Attribute`.
  - Message format: `'{0}' derives from ValidationAttribute but the generator cannot emit it; derive from ValidationAttribute<T> and override IsValid`, where `{0}` is the attribute class name.
- **ZV0021 (Error):** a custom rule whose property type has no implicit conversion to `T`.
  - The check is `compilation.ClassifyConversion(prop.Type, valueType).IsImplicit`.
  - The generator needs the `Compilation`. Check how `RuleEmitter` can reach it: via `classSymbol` there is none, so thread `Compilation` from `ValidatorGenerator`, where the provider already has it; find `CompilationProvider` or `Combine` in `ValidatorGenerator.cs`.
  - When it fails, report ZV0021 and do not emit that rule: skip it in the rules list before the emit loops, so field indices stay stable. Build `ruleIndex` from the filtered list, and make both the sync and async visits apply the same filter.
  - Message format: `'{0}' validates '{1}' but property '{2}' is '{3}'`, with the attribute class name, `T` in display form, the property name and the property type in display form.
- **ZV0023 (Error):** a custom rule whose attribute type, constructor, or any named property set on the usage is not accessible from the generated validator.
  - The validator is a separate class in the same assembly. Check with `compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly)` for the attribute class, including its containing types, then for `attr.AttributeConstructor`, then for each named argument's property or field symbol.
  - When the check fails, report ZV0023 at the attribute location and skip the rule, using the same filtering as ZV0021, so no field is emitted.
  - Message format: `'{0}' is not accessible from the generated validator; make the attribute type and its constructor internal or public`, where `{0}` is the attribute class name.
  - Descriptor line for `AnalyzerReleases.Unshipped.md`: `ZV0023  | ZeroAlloc.Validation | Error    | Custom rule attribute not accessible from the generated validator`.
  - Test `Private_nested_attribute_reports_ZV0023_and_emits_no_field`: a `private sealed class Hidden : ValidationAttribute<string?>` nested inside the `[Validate]` model and used on its property. Assert one ZV0023, no `__Rule_` field, and no CS0122 in the output compilation. Also add a `protected` nested variant.
- **Duplicates:** each diagnostic must be reported once per usage, on the `ctx` visit only, as with ZV0022.

- [ ] **Step 1: Write failing tests:**
  - `Non_generic_ValidationAttribute_subclass_reports_ZV0020`: the issue's original `sealed class NotBlankAttribute : ValidationAttribute { }` on a `string?` property. Assert exactly one ZV0020 at Error severity, and that its location span text is `NotBlank`.
  - `ZV0020_not_reported_for_builtins_or_generic_rules`: a model with `[NotEmpty]`, `[Must(nameof(M))]` and a `ValidationAttribute<string?>` rule. Assert no ZV0020.
  - `Type_mismatch_reports_ZV0021_and_skips_rule`: `[NotBlank]`, validating `string?`, on an `int` property. Assert one ZV0021 at Error severity whose message contains `'string?'` or `'string'` (assert on the display form actually produced) and `'int'`. Assert that the generated source contains no `__Rule_Count_`.
  - `Implicit_conversion_is_accepted`: an `int` property with a `ValidationAttribute<long>` rule. Assert no ZV0021 and that the code compiles.
  - `Nullable_value_to_non_nullable_is_rejected`: an `int?` property with a `ValidationAttribute<int>` rule. Assert ZV0021.

- [ ] **Step 2: Run the tests; expect them to fail.** Run the same filter as before.

- [ ] **Step 3: Implement.** Add the descriptors next to ZV0016, and add the lines to `AnalyzerReleases.Unshipped.md`:
```
ZV0020  | ZeroAlloc.Validation | Error    | ValidationAttribute subclass the generator cannot emit
ZV0021  | ZeroAlloc.Validation | Error    | Custom rule value type does not match the property type
```

- [ ] **Step 4: Run the tests; expect them to pass.** Run `dotnet test -c Release` over the whole solution. Every test project must pass, including Options, AspNetCore, Inject, MSTest and NUnit. DuplicateGeneratorTests needs the packed local feed; follow its README or CI step.

- [ ] **Step 5: Commit.** `git commit -m "feat(generator)!: report ZV0020 and ZV0021 for custom rules the generator cannot emit"`

---

### Task 5: PackSmoke, docs and the final commit

**Files:**
- Create: `tests/ZeroAlloc.Validation.PackSmoke/CustomRulePackTests.cs`
- Modify: `docs/custom-validation.md`, `docs/diagnostics.md`, `docs/migrating-to-v2.md`

- [ ] **Step 1: PackSmoke test.**
  - Follow `GeneratedAccessibilityPackTests.cs`: its use of `PackedFeed`, consumer project generation and build.
  - The consumer references `ZeroAlloc.Validation` only. It defines `NotBlankAttribute : ValidationAttribute<string?>` with `[RuleMessage("{PropertyName} must not be blank.")]` and a `[Validate]` model using it. Its `Program.Main` validates `new Model { Name = "  " }`, prints the failure message, and exits 1 if there is no failure.
  - Assert the build succeeds, the run exits 0, and stdout contains `Name must not be blank.`.
  - If the existing tests only build without running, assert on the build, and use reflection over the built DLL to check that the validator type has a static field of type `NotBlankAttribute`.
  - Run: `dotnet test tests/ZeroAlloc.Validation.PackSmoke -c Release`.

- [ ] **Step 2: Docs.**
  - `docs/custom-validation.md`: add a "Custom rule attributes" section. Cover the `ValidationAttribute<T>` example (`NotBlank`, `MinWords`), `[RuleMessage]` and its precedence, placeholders, the note on allocation and AOT (one static instance per usage), the rule that the property type must convert implicitly, the fact that collections validate the property itself, and when to prefer `[Must]` (model-specific checks).
  - `docs/diagnostics.md`: add ZV0020, ZV0021 and ZV0022, in the format of the existing entries.
  - `docs/migrating-to-v2.md`: add an entry. A `ValidationAttribute` subclass used on a validated property now fails with ZV0020, because it was previously ignored silently and the property went unvalidated. Tell readers to derive from `ValidationAttribute<T>` and override `IsValid`, or to remove the attribute.

- [ ] **Step 3: Full verification.** Build the solution with 0 warnings, run every test project, and run the PackSmoke tests.

- [ ] **Step 4: Squash to one commit.** Run `git reset --soft origin/main` and commit everything, including the two docs/plans files, with this message:

```
feat!: support user-defined rule attributes via ValidationAttribute<T>

Custom property rules derive from ValidationAttribute<T> and override IsValid. The generator
rebuilds each usage once as a private static readonly field on the validator and calls
IsValid with the property value: statically bound, no reflection, nothing allocated per
validation. Message, ErrorCode, Severity, When and Unless, and stop-on-first-failure work
as for built-in rules.

RuleMessage on the attribute class sets the default message and error code. Placeholders
resolve at compile time from PropertyName, PropertyValue, and the constructor arguments or
named properties written on the usage. An unknown placeholder reports ZV0022.

ZV0021 reports a rule whose value type the property cannot convert to implicitly.

BREAKING CHANGE: a ValidationAttribute subclass that does not derive from
ValidationAttribute<T> now fails the build with ZV0020. It was previously ignored without
a diagnostic, so the property it decorated was never validated.

Closes #196

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

  Check the message: `git log -1 --format=%B | awk 'length>100'` must print nothing, and there must be no nested parentheses.
