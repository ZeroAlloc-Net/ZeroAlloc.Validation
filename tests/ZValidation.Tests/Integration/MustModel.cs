using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public partial class MustModel
{
    [Must(nameof(CodeStartsWithW), Message = "Code must start with W")]
    public string Code { get; set; } = "";

    public bool CodeStartsWithW(string value) => value.StartsWith("W", System.StringComparison.Ordinal);
}
