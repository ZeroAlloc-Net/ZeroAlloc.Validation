namespace ZValidation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class DisplayNameAttribute : Attribute
{
    public DisplayNameAttribute(string displayName)
    {
        DisplayName = displayName;
    }

    public string DisplayName { get; }
}
