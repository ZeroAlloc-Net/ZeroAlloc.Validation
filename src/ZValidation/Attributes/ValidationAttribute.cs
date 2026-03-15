namespace ZValidation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class ValidationAttribute : Attribute
{
    public string? Message { get; set; }
}
