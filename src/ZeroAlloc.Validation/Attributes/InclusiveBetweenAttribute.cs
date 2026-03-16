namespace ZValidation;

public sealed class InclusiveBetweenAttribute(double min, double max) : ValidationAttribute
{
    public double Min { get; } = min;
    public double Max { get; } = max;
}
