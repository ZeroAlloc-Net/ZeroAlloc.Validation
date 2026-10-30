using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocCodePointLengthModel
{
    [MaxLength(4, Unit = TextLengthUnit.CodePoints)]
    public string Max { get; set; } = "";

    [Length(2, 4, Unit = TextLengthUnit.CodePoints)]
    public string Range { get; set; } = "";
}
