namespace ZValidation;

public sealed class LengthAttribute(int min, int max) : ValidationAttribute
{
    public int Min { get; } = min;
    public int Max { get; } = max;
}
