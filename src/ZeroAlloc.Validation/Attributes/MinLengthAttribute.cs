namespace ZeroAlloc.Validation;

public sealed class MinLengthAttribute(int min) : ValidationAttribute
{
    public int Min { get; } = min;
}
