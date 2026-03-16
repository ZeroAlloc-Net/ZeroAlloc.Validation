namespace ZValidation;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ValidateAttribute : Attribute
{
    public bool StopOnFirstFailure { get; set; }
}
