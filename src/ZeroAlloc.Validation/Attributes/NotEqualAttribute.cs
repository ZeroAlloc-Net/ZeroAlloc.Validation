namespace ZValidation;

public sealed class NotEqualAttribute : ValidationAttribute
{
    public NotEqualAttribute(double value) { NumericValue = value; }
    public NotEqualAttribute(string value) { StringValue = value; }
    public double NumericValue { get; }
    public string? StringValue { get; }
}
