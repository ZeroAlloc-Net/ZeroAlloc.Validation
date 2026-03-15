namespace ZValidation;

public readonly struct ValidationFailure
{
    public string PropertyName { get; init; }
    public string ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public Severity Severity { get; init; }
}
