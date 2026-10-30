using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// The length rules counting code points, #361, next to one counting UTF-16 code units, the
/// default, so the tests can show where the two disagree.
/// </summary>
[Validate]
public class CodePointLengthModel
{
    [MaxLength(2, Unit = TextLengthUnit.CodePoints)]
    public string? Max { get; set; }

    [MinLength(2, Unit = TextLengthUnit.CodePoints)]
    public string? Min { get; set; }

    [Length(2, 3, Unit = TextLengthUnit.CodePoints)]
    public string? Range { get; set; }

    [MaxLength(2)]
    public string? MaxCodeUnits { get; set; }
}
