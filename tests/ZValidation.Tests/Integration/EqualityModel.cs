using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class EqualityModel
{
    [Equal("active", Message = "Status must be active.")]
    public string Status { get; set; } = "";

    [NotEqual(0.0)]
    public double Score { get; set; }
}
