namespace ZValidation;

public readonly struct ValidationResult
{
    private readonly ValidationFailure[] _failures;

    public ValidationResult(ValidationFailure[] failures)
    {
        _failures = failures;
    }

    public bool IsValid => _failures is null || _failures.Length == 0;
    public ReadOnlySpan<ValidationFailure> Failures => _failures ?? [];
}
