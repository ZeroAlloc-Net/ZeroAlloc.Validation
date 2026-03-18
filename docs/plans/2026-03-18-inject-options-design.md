# Inject & Options Design

## Goal

Add zero-friction DI registration and options validation to ZeroAlloc.Validation. Users should
need zero manual DI registrations — installing the right package and calling one method is all
that is required.

## Packages

Two new packages, plus changes to the existing AspNetCore package.

| Package | Type | Purpose |
|---|---|---|
| `ZeroAlloc.Validation.Inject` | Generator only | Emits `AddZeroAllocValidators()` — bulk validator registration |
| `ZeroAlloc.Validation.Options` | Generator + runtime | Emits `ValidateWithZeroAlloc()` overloads + options adapter |
| `ZeroAlloc.Validation.AspNetCore` | Existing — updated | Rename Z-prefixed names; `AddZeroAllocValidation()` auto-registers validators |

## Design Principles

- **Zero manual DI registrations** — installing the right package and calling one method is enough.
- **`TryAddSingleton` throughout** — all registrations are idempotent; order does not matter.
- **Validators remain DI-agnostic** — `new DatabaseOptionsValidator()` always works; DI is opt-in.
- **Shared emit helper** — validator registration emit logic lives in one place and is referenced
  by both the `Inject` generator and the `AspNetCore.Generator`. Not duplicated.
- **Clean rename** — all `Z`-prefixed generated names replaced with `ZeroAlloc`-prefixed names.
  No `[Obsolete]` shim — clean cut.

## ZeroAlloc.Validation.Inject

Generator-only package (`netstandard2.0`). No runtime classes.

Scans all `[Validate]` classes in the compilation and emits into the consuming assembly:

```csharp
// generated
public static class ZeroAllocValidatorRegistrationExtensions
{
    public static IServiceCollection AddZeroAllocValidators(this IServiceCollection services)
    {
        services.TryAddSingleton<ValidatorFor<DatabaseOptions>, DatabaseOptionsValidator>();
        services.TryAddSingleton<ValidatorFor<SmtpOptions>, SmtpOptionsValidator>();
        // one line per [Validate] class
        return services;
    }
}
```

Registers as `ValidatorFor<T>` so both `.Options` and `.AspNetCore` can resolve by the
abstract base type.

**Dependencies:**
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `ZeroAlloc.Validation` (to reference `ValidatorFor<T>`)

## ZeroAlloc.Validation.Options

Has its own generator that reuses the shared registration emit helper from the `Inject` generator
project. Targets `net8.0;net9.0;net10.0`.

### Generated code

For each `[Validate]` class, emits a strongly-typed overload of `ValidateWithZeroAlloc()`:

```csharp
// generated per [Validate] class
public static OptionsBuilder<DatabaseOptions> ValidateWithZeroAlloc(
    this OptionsBuilder<DatabaseOptions> builder)
{
    builder.Services.TryAddSingleton<ValidatorFor<DatabaseOptions>, DatabaseOptionsValidator>();
    builder.Services.TryAddSingleton<IValidateOptions<DatabaseOptions>,
        ZeroAllocOptionsValidator<DatabaseOptions>>();
    return builder;
}
```

No generic fallback — if a class does not have `[Validate]`, no overload is generated and the
compiler reports the error at build time.

### Runtime class

```csharp
public sealed class ZeroAllocOptionsValidator<T> : IValidateOptions<T> where T : class
{
    private readonly ValidatorFor<T> _validator;

    public ZeroAllocOptionsValidator(ValidatorFor<T> validator) => _validator = validator;

    public ValidateOptionsResult Validate(string? name, T options)
    {
        var result = _validator.Validate(options);
        if (result.IsValid) return ValidateOptionsResult.Success;

        var errors = new string[result.Failures.Length];
        for (int i = 0; i < result.Failures.Length; i++)
            errors[i] = $"{result.Failures[i].PropertyName}: {result.Failures[i].ErrorMessage}";
        return ValidateOptionsResult.Fail(errors);
    }
}
```

**Dependencies:**
- `Microsoft.Extensions.Options`
- `ZeroAlloc.Validation`

## ZeroAlloc.Validation.AspNetCore — Changes

### Renames (breaking, clean cut)

| Old | New |
|---|---|
| `ZValidationActionFilter` | `ZeroAllocValidationActionFilter` |
| `ZValidationServiceCollectionExtensions` | `ZeroAllocValidationServiceCollectionExtensions` |
| `AddZValidationAutoValidation()` | `AddZeroAllocValidation()` |

### AddZeroAllocValidation() — now includes validator registration

The `AspNetCoreFilterEmitter` is updated to use the shared registration emit helper. The
generated `AddZeroAllocValidation()` method emits validator `TryAdd` lines alongside the
existing filter wiring:

```csharp
// generated
public static IServiceCollection AddZeroAllocValidation(this IServiceCollection services)
{
    // validator registrations (via shared emit helper — same logic as .Inject)
    services.TryAddSingleton<ValidatorFor<CreateOrderRequest>, CreateOrderRequestValidator>();
    services.TryAddSingleton<ValidatorFor<UpdateUserRequest>, UpdateUserRequestValidator>();

    // filter wiring (existing)
    services.TryAddTransient<ZeroAllocValidationActionFilter>();
    services.Configure<MvcOptions>(o => o.Filters.Add<ZeroAllocValidationActionFilter>());
    return services;
}
```

## Usage

### ASP.NET Core app — one call, everything works

```csharp
builder.Services.AddZeroAllocValidation();

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateWithZeroAlloc()
    .ValidateOnStart();
```

### Console app / worker service — validators in DI only

```csharp
services.AddZeroAllocValidators();
```

### All three packages installed — fully idempotent

```csharp
builder.Services.AddZeroAllocValidators();    // TryAdd
builder.Services.AddZeroAllocValidation();    // TryAdd — no duplicates
builder.Services.AddOptions<DatabaseOptions>()
    .ValidateWithZeroAlloc();                 // TryAdd — no duplicates
```

## Shared Emit Helper

A single internal class `ValidatorRegistrationEmitter` lives in the `Inject` generator project.
Both `ZeroAlloc.Validation.AspNetCore.Generator` and the new `Options` generator project
reference the `Inject` generator project directly (not the NuGet package) to access this helper.

```csharp
// in ZeroAlloc.Validation.Inject (generator project)
internal static class ValidatorRegistrationEmitter
{
    /// <summary>
    /// Appends one TryAddSingleton line per model into <paramref name="sb"/>.
    /// </summary>
    public static void EmitRegistrations(
        StringBuilder sb,
        IEnumerable<(string ModelFqn, string ValidatorFqn)> models)
    {
        foreach (var (model, validator) in models)
            sb.AppendLine(
                $"        services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<{model}>, {validator}>();");
    }
}
```

## New Projects

| Project file | Notes |
|---|---|
| `src/ZeroAlloc.Validation.Inject/ZeroAlloc.Validation.Inject.csproj` | `netstandard2.0`, `IsRosylnComponent=true` |
| `src/ZeroAlloc.Validation.Options/ZeroAlloc.Validation.Options.csproj` | `net8.0;net9.0;net10.0` runtime |
| `src/ZeroAlloc.Validation.Options.Generator/ZeroAlloc.Validation.Options.Generator.csproj` | `netstandard2.0`, generator |

## Non-Goals

- No change to `[Validate]`, validation attributes, or `ValidationResult` / `ValidationFailure`
- No change to `ZeroAlloc.Validation.Testing`
- No reflection at runtime — all registration is compile-time generated
- No support for options classes without `[Validate]`
- Validators remain usable without DI (`new MyValidator()` always works)
