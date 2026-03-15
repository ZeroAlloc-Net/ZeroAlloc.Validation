namespace ZValidation;

public sealed class MustAttribute(string methodName) : ValidationAttribute
{
    public string MethodName { get; } = methodName;
}
