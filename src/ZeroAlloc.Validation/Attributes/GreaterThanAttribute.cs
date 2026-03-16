namespace ZeroAlloc.Validation;

public sealed class GreaterThanAttribute(double value) : ValidationAttribute
{
    public double Value { get; } = value;
}
