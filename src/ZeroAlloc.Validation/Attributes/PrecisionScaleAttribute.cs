namespace ZeroAlloc.Validation;

public sealed class PrecisionScaleAttribute(int precision, int scale) : ValidationAttribute
{
    public int Precision { get; } = precision;
    public int Scale { get; } = scale;
}
