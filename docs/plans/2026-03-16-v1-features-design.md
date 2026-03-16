# v1 Features Design: §4.3 Custom Validation, §15 SkipWhen, §20 ArrayPool Mixed Path

## Goal

Three independent features shipped as a v1 milestone:

1. **§4.3 `[CustomValidation]`** — attribute-on-method hook for cross-property multi-failure custom logic
2. **§15 `[SkipWhen]`** — class-level skip guard; returns empty valid result when condition is true
3. **§20 `FailureBuffer`** — replace `List<ValidationFailure>` in the mixed emit path with an `ArrayPool`-backed ref struct; reduces mixed-path allocations from 2+ to 1 (failure path) or 0 (valid path)

---

## Feature 1: `[CustomValidation]`

### API

`[CustomValidation]` placed directly on an instance method of the model. No `nameof` reference needed — the attribute lives on the method itself.

```csharp
[Validate]
public class Order
{
    [NotEmpty]
    public string Reference { get; set; } = "";

    [NotNull]
    public Address? ShippingAddress { get; set; }

    [CustomValidation]
    private IEnumerable<ValidationFailure> ValidateBusinessRules()
    {
        if (Reference.StartsWith("TEST", StringComparison.Ordinal) && ShippingAddress is null)
            yield return new ValidationFailure
            {
                PropertyName = nameof(ShippingAddress),
                ErrorMessage = "TEST orders require a shipping address."
            };
    }
}
```

### Behavior

- Method signature must be `IEnumerable<ValidationFailure> MethodName()` — zero parameters, returns `IEnumerable<ValidationFailure>`
- Multiple `[CustomValidation]` methods allowed; called in declaration order
- Called **after** all property rules and nested/collection validators have run
- A model with any `[CustomValidation]` method is always routed to the mixed emit path
- **ZV0013** (error) emitted if the decorated method has the wrong return type or parameters

### Generated code shape

```csharp
foreach (var _cf in instance.ValidateBusinessRules())
    _buf.Add(_cf);
```

### Files changed

- Create: `src/ZeroAlloc.Validation/Attributes/CustomValidationAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

---

## Feature 2: `[SkipWhen]`

### API

`[SkipWhen(nameof(Method))]` on the class. The referenced method is a zero-parameter `bool` instance method on the model.

```csharp
[Validate]
[SkipWhen(nameof(ShouldSkipValidation))]
public class Order
{
    [NotEmpty]
    public string Reference { get; set; } = "";

    public bool IsDraft { get; set; }
    private bool ShouldSkipValidation() => IsDraft;
}
```

### Behavior

- Emitted as the very first check in `Validate` — before `[SkipWhen]` guard, any property rules, nested validators, or `[CustomValidation]` calls
- Returns `new ValidationResult(Array.Empty<ValidationFailure>())` — empty valid result, no failures
- `AllowMultiple = false` — only one `[SkipWhen]` per class
- Method not found / wrong signature → compile error in generated code (no special diagnostic needed; the error message is self-explanatory)

### Generated code shape

```csharp
public override ValidationResult Validate(Order instance)
{
    if (instance.ShouldSkipValidation())
        return new global::ZeroAlloc.Validation.ValidationResult(global::System.Array.Empty<global::ZeroAlloc.Validation.ValidationFailure>());

    // ... rest of validation ...
}
```

### Files changed

- Create: `src/ZeroAlloc.Validation/Attributes/SkipWhenAttribute.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

---

## Feature 3: `FailureBuffer` — ArrayPool mixed path

### Problem

The mixed emit path (models with nested/collection validators, or `[CustomValidation]` methods) currently allocates:
1. `new List<ValidationFailure>()` — one object allocation
2. List's internal array — second allocation
3. `failures.ToArray()` — third allocation (final result)

On the valid path (no failures), allocations 1–2 still occur.

### Solution

Replace `List<T>` with a `FailureBuffer` ref struct in `ZeroAlloc.Validation.Internal` that wraps an `ArrayPool<ValidationFailure>` buffer.

```csharp
namespace ZeroAlloc.Validation.Internal;

internal ref struct FailureBuffer
{
    private static readonly System.Buffers.ArrayPool<ZeroAlloc.Validation.ValidationFailure> Pool =
        System.Buffers.ArrayPool<ZeroAlloc.Validation.ValidationFailure>.Shared;

    private ZeroAlloc.Validation.ValidationFailure[] _buf;
    private int _count;

    public FailureBuffer(int initialCapacity)
    {
        _buf = Pool.Rent(initialCapacity < 4 ? 4 : initialCapacity);
        _count = 0;
    }

    public int Count => _count;

    public void Add(ZeroAlloc.Validation.ValidationFailure f)
    {
        if (_count == _buf.Length) Grow();
        _buf[_count++] = f;
    }

    private void Grow()
    {
        var newBuf = Pool.Rent(_buf.Length * 2);
        System.Array.Copy(_buf, newBuf, _count);
        Pool.Return(_buf, clearArray: false);
        _buf = newBuf;
    }

    public ZeroAlloc.Validation.ValidationResult ToResult()
    {
        if (_count == 0)
        {
            Pool.Return(_buf, clearArray: false);
            _buf = System.Array.Empty<ZeroAlloc.Validation.ValidationFailure>();
            return new ZeroAlloc.Validation.ValidationResult(System.Array.Empty<ZeroAlloc.Validation.ValidationFailure>());
        }
        var result = new ZeroAlloc.Validation.ValidationFailure[_count];
        System.Array.Copy(_buf, result, _count);
        Pool.Return(_buf, clearArray: false);
        _buf = System.Array.Empty<ZeroAlloc.Validation.ValidationFailure>();
        return new ZeroAlloc.Validation.ValidationResult(result);
    }
}
```

Setting `_buf` to `Array.Empty` after returning to pool ensures idempotent behavior if `ToResult()` were ever called twice (e.g. from `StopOnFirstFailure` early returns — each early return calls `ToResult()` and the method exits immediately, so double-call cannot occur in practice, but the guard is cheap insurance).

### Generated code shape

```csharp
// Mixed path — before:
var failures = new System.Collections.Generic.List<global::ZeroAlloc.Validation.ValidationFailure>();
// ... failures.Add(...) ...
return new global::ZeroAlloc.Validation.ValidationResult(failures.ToArray());

// Mixed path — after:
var _buf = new global::ZeroAlloc.Validation.Internal.FailureBuffer(N);  // N = totalDirectRules
// ... _buf.Add(...) ...
return _buf.ToResult();
```

`StopOnFirstFailure` (nested path) early returns become `return _buf.ToResult()` — identical to the terminal return.

### Allocation profile

| Path | Before | After |
|------|--------|-------|
| Valid (no failures) | 2 allocations (`List` + internal array) | 0 allocations |
| Failure | 3 allocations (`List` + internal array + `ToArray()`) | 1 allocation (final `ValidationFailure[]`) |

### Files changed

- Create: `src/ZeroAlloc.Validation/Internal/FailureBuffer.cs`
- Modify: `src/ZeroAlloc.Validation.Generator/RuleEmitter.cs`

---

## Interaction between features

- `[CustomValidation]` forces the mixed path → always uses `FailureBuffer`
- `[SkipWhen]` is emitted before `FailureBuffer` is initialized — short-circuits before any allocation
- `[SkipWhen]` + `[CustomValidation]` compose correctly: if `SkipWhen` fires, `FailureBuffer` is never constructed

---

## Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| ZV0013 | Error | `[CustomValidation]` method must return `IEnumerable<ValidationFailure>` and have no parameters |

---

## Testing

### Generator emission tests

- `[CustomValidation]` present → generated code calls method in foreach after nested validators
- Multiple `[CustomValidation]` methods → both called, in declaration order
- `[SkipWhen]` present → generated code starts with the if-guard returning `Array.Empty`
- `[SkipWhen]` absent → no skip guard (regression)
- Mixed path → `FailureBuffer(N)` emitted instead of `List<>`
- Flat path → unchanged (no `FailureBuffer`, no regression)
- ZV0013 → emitted for wrong `[CustomValidation]` method signature

### Integration tests

- `[CustomValidation]` failures appear in result
- `[CustomValidation]` failures compose with property-level failures
- Multiple `[CustomValidation]` methods: all failures reported
- `[SkipWhen]` true → no failures, `IsValid = true`
- `[SkipWhen]` false → normal validation runs
- Valid model on mixed path → `result.Failures.Length == 0`, zero heap allocations (BenchmarkDotNet or manual GC count check)
- Failure model on mixed path → one allocation
