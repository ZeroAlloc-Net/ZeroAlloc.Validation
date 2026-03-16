using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class PropertyValueModel
{
    // Non-nullable value type
    [GreaterThan(0, Message = "Age must be > 0, got {PropertyValue}.")]
    public int Age { get; set; }

    // String
    [MaxLength(5, Message = "Name '{PropertyValue}' exceeds 5 characters.")]
    public string Name { get; set; } = "";

    // Nullable value type
    [GreaterThan(0, Message = "Score must be > 0, got {PropertyValue}.")]
    public double? Score { get; set; }

    // Mixed with {PropertyName} compile-time placeholder
    [GreaterThan(100, Message = "{PropertyName} must be > 100, got {PropertyValue}.")]
    public int Points { get; set; }

    // No {PropertyValue} — regression guard
    [MinLength(2, Message = "Too short.")]
    public string Code { get; set; } = "";
}
