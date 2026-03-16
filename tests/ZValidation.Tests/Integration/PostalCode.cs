using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class PostalCode
{
    [NotEmpty(Message = "Code is required.", ErrorCode = "CODE_REQUIRED")]
    public string Code { get; set; } = "";
}
