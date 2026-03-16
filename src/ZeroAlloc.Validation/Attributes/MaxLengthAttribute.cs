namespace ZValidation;

public sealed class MaxLengthAttribute(int max) : ValidationAttribute
{
    public int Max { get; } = max;
}
