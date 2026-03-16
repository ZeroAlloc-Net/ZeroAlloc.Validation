using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class NullEmptyModel
{
    [Null]
    public string? MustBeNull { get; set; }

    [Empty(Message = "Must be empty.")]
    public string MustBeEmpty { get; set; } = "";
}
