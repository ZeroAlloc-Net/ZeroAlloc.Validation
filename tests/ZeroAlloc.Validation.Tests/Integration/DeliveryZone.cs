using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class DeliveryZone
{
    [NotEmpty(Message = "Zone name is required.")]
    public string Name { get; set; } = "";

    public PostalCode PostalCode { get; set; } = new();
}
