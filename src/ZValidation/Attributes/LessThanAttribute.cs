namespace ZValidation;

public sealed class LessThanAttribute(double value) : ValidationAttribute
{
    public double Value { get; } = value;
}
