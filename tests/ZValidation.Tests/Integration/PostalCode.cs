using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class PostalCode
{
    [NotEmpty(Message = "Code is required.")]
    public string Code { get; set; } = "";
}
