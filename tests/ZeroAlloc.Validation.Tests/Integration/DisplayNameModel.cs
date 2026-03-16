using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class DisplayNameModel
{
    [DisplayName("First Name")]
    [NotEmpty]
    [MinLength(2)]
    public string Forename { get; set; } = "ok";

    [DisplayName("ZIP Code")]
    [Matches(@"^\d{5}$", Message = "{PropertyName} must be 5 digits.")]
    public string ZipCode { get; set; } = "12345";

    // No [DisplayName] — raw property name used
    [NotEmpty]
    public string NoDisplayName { get; set; } = "ok";
}
