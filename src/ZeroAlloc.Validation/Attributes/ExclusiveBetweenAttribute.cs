namespace ZeroAlloc.Validation;

public sealed class ExclusiveBetweenAttribute(double min, double max) : ValidationAttribute
{
    public double Min { get; } = min;
    public double Max { get; } = max;
}
