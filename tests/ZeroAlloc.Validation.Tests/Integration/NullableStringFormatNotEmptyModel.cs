using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>[NotEmpty] paired with a format rule: a null is reported once, by [NotEmpty].</summary>
[Validate]
public class NullableStringFormatNotEmptyModel
{
    [NotEmpty]
    [EmailAddress]
    public string? Email { get; set; }

    [NotEmpty]
    [Matches(@"^\d+$")]
    public string? Digits { get; set; }
}
