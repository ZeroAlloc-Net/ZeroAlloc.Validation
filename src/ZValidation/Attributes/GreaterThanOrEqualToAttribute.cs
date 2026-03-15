namespace ZValidation;

public sealed class GreaterThanOrEqualToAttribute(double value) : ValidationAttribute
{
    public double Value { get; } = value;
}
