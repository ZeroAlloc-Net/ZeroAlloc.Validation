using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class PostalCode
{
    [NotEmpty(Message = "Code is required.", ErrorCode = "CODE_REQUIRED")]
    public string Code { get; set; } = "";
}
