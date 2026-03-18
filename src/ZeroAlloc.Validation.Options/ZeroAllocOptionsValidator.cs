using Microsoft.Extensions.Options;

namespace ZeroAlloc.Validation.Options;

/// <summary>
/// Bridges a <see cref="ValidatorFor{T}"/> into the <see cref="IValidateOptions{T}"/> pipeline.
/// Resolved from DI by the generated <c>ValidateWithZeroAlloc()</c> extension methods.
/// </summary>
public sealed class ZeroAllocOptionsValidator<T> : IValidateOptions<T> where T : class
{
    private readonly ValidatorFor<T> _validator;

    public ZeroAllocOptionsValidator(ValidatorFor<T> validator) => _validator = validator;

    public ValidateOptionsResult Validate(string? name, T options)
    {
        var result = _validator.Validate(options);
        if (result.IsValid)
            return ValidateOptionsResult.Success;

        var failures = result.Failures;
        var errors   = new string[failures.Length];
        for (int i = 0; i < failures.Length; i++)
            errors[i] = $"{failures[i].PropertyName}: {failures[i].ErrorMessage}";

        return ValidateOptionsResult.Fail(errors);
    }
}
