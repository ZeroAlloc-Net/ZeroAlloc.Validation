using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

[Validate]
public sealed class Address
{
    [NotEmpty(Message = "Street is required.")]
    public string Street { get; set; } = "";

    [NotEmpty]
    public string City { get; set; } = "";
}
