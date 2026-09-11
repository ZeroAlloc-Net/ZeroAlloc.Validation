using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocDerivedModel : AllocBaseModel
{
    [NotEmpty]
    public string Code { get; set; } = "";
}
