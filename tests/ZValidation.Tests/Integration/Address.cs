using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class Address
{
    [NotEmpty(Message = "Street is required.", ErrorCode = "STREET_REQUIRED")]
    public string Street { get; set; } = "";

    [NotEmpty(Message = "City is required.", Severity = Severity.Warning)]
    public string City { get; set; } = "";
}
