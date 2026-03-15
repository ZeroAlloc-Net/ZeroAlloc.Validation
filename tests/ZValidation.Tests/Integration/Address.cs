using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class Address
{
    [NotEmpty(Message = "Street is required.")]
    public string Street { get; set; } = "";

    [NotEmpty(Message = "City is required.")]
    public string City { get; set; } = "";
}
