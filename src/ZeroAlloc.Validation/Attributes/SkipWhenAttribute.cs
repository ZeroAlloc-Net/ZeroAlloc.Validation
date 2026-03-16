namespace ZValidation;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class SkipWhenAttribute : Attribute
{
    public SkipWhenAttribute(string methodName)
    {
        MethodName = methodName;
    }

    public string MethodName { get; }
}
