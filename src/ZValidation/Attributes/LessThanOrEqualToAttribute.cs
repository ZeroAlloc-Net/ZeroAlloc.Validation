namespace ZValidation;

public sealed class LessThanOrEqualToAttribute(double value) : ValidationAttribute
{
    public double Value { get; } = value;
}
