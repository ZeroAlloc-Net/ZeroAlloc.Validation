using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public class AllocBaseModel
{
    [NotEmpty]
    public string Name { get; set; } = "";
}
