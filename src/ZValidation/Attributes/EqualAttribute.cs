namespace ZValidation;

public sealed class EqualAttribute : ValidationAttribute
{
    public EqualAttribute(double value) { NumericValue = value; }
    public EqualAttribute(string value) { StringValue = value; }
    public double NumericValue { get; }
    public string? StringValue { get; }
}
