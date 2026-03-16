using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class MatchesModel
{
    [Matches(@"^\d{5}$", Message = "ZipCode must be exactly 5 digits.")]
    public string ZipCode { get; set; } = "";

    [Matches(@"^[A-Z]{2,3}$")]
    public string CountryCode { get; set; } = "";
}
