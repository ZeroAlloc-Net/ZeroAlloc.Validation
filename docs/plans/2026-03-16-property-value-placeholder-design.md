# {PropertyValue} Placeholder Design

## Goal

Add `{PropertyValue}` as a runtime placeholder in custom `Message` strings, allowing error messages to include the actual value that failed validation (e.g. `"Age must be > 0, got -5."`).

## Approach

Option A — inline interpolation. The generator detects `{PropertyValue}` in a custom message at code-gen time and emits a C# interpolated string that references the property on the model instance. All other placeholders (`{PropertyName}`, `{ComparisonValue}`, etc.) are still substituted at code-gen time as string literals first; `{PropertyValue}` is resolved last as an interpolation hole.

## Architecture

Only `src/ZValidation.Generator/RuleEmitter.cs` changes. No attribute classes, no runtime library, no public API additions.

### Placeholder resolution order

1. Compile-time substitution: replace `{PropertyName}`, `{ComparisonValue}`, `{MinLength}`, `{MaxLength}`, `{From}`, `{To}` with their known string values — same as today.
2. Runtime substitution: if `{PropertyValue}` remains after step 1, switch the `ErrorMessage` initializer from a plain string literal to a C# interpolated string, replacing `{PropertyValue}` with the appropriate property access expression.

### Emitted expression by property type

| Property type | Emitted interpolation hole |
|---------------|---------------------------|
| Non-nullable value type (`int`, `double`, `decimal`, `bool`, enum, …) | `{instance.Prop}` |
| Nullable value type (`int?`, `double?`, …) | `{instance.Prop?.ToString() ?? "null"}` |
| `string` | `{instance.Prop ?? "null"}` |
| Any other reference type | `{instance.Prop?.ToString() ?? "null"}` |

### Example

```csharp
[GreaterThan(0, Message = "{PropertyName} must be > {ComparisonValue}, got {PropertyValue}.")]
public int Age { get; set; }
```

Generated (step 1 substitutes `{PropertyName}` → `"Age"`, `{ComparisonValue}` → `"0"`; step 2 replaces `{PropertyValue}`):

```csharp
ErrorMessage = $"Age must be > 0, got {instance.Age}."
```

Mixed placeholders:

```csharp
[MaxLength(100, Message = "'{PropertyValue}' exceeds the {MaxLength}-character limit for {PropertyName}.")]
public string Bio { get; set; } = "";
// → ErrorMessage = $"'{instance.Bio ?? "null"}' exceeds the 100-character limit for Bio."
```

## Allocation profile

- **Happy path (valid data):** zero allocation — the interpolated string expression is never evaluated.
- **Failure path:** one `string` allocation per failure when `{PropertyValue}` is in the message. For `string` properties the allocation is the interpolated string itself; for value types it also includes a `ToString()` call. This is an intentional, documented trade-off.

## Opt-in only

No default messages are modified. `{PropertyValue}` only activates when the user includes it in a custom `Message` named parameter. Existing validators behave identically.

## Edge cases

- **Repeated `{PropertyValue}`** — each occurrence emits the same interpolation hole; no special handling needed.
- **Complex/reference type properties** — emits `.ToString() ?? "null"`; the user is responsible for a meaningful `ToString()` implementation.
- **Mixed placeholders** — all compile-time placeholders are resolved first, then `{PropertyValue}` last; they compose correctly.
- **No custom message** — `{PropertyValue}` is never present in default messages; generator path is unchanged.

## No new diagnostics

Using `{PropertyValue}` on any property type is valid. The generator does not warn about complex types with potentially uninformative `ToString()` output.

## Testing

### Generator emission tests (`GeneratorRuleEmissionTests.cs`)

Verify emitted code for each type category:
- Non-nullable value type → `$"...{instance.Prop}..."`
- `string` → `$"...{instance.Prop ?? "null"}..."`
- Nullable value type → `$"...{instance.Prop?.ToString() ?? "null"}..."`
- Multiple `{PropertyValue}` in one message → two interpolation holes
- Mixed with compile-time placeholders → compile-time ones substituted, `{PropertyValue}` as hole

### Integration tests (`PropertyValuePlaceholderTests.cs`)

Verify actual runtime messages:
- Value type failure: message contains the string representation of the failing value
- String failure: message contains the actual string value
- Null property: message contains `"null"`
- No `{PropertyValue}` in message: unaffected (regression guard)
