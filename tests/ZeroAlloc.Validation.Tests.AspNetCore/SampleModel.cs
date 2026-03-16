using ZValidation;

namespace ZValidation.Tests.AspNetCore;

[Validate]
public partial class SampleModel
{
    [NotEmpty] public string Name { get; set; } = "";
    [GreaterThan(0)] public int Quantity { get; set; }
}
