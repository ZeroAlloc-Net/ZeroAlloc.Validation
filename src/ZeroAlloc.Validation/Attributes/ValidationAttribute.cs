namespace ZeroAlloc.Validation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class ValidationAttribute : Attribute
{
    public string? Message   { get; set; }
    public string? When      { get; set; }
    public string? Unless    { get; set; }
    public string? ErrorCode { get; set; }
    public Severity Severity { get; set; } = Severity.Error;
}
