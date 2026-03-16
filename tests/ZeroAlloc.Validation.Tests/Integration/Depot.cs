using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class Depot
{
    [NotEmpty]
    public string Id { get; set; } = "";

    public DeliveryZone Zone { get; set; } = new();
}
