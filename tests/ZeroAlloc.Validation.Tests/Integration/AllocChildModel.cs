using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocChildModel
{
    [NotEmpty]
    public string Code { get; set; } = "";
}
