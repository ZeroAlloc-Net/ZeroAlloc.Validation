namespace ZeroAlloc.Validation;

public sealed class MaxLengthAttribute(int max) : ValidationAttribute
{
    public int Max { get; } = max;
}
