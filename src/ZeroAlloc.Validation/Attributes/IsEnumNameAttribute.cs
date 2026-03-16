namespace ZValidation;

public sealed class IsEnumNameAttribute(Type enumType) : ValidationAttribute
{
    public Type EnumType { get; } = enumType;
}
