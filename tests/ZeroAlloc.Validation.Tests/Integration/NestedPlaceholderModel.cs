using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Values substituted into a message that themselves contain placeholder tokens. Each value must
/// appear exactly as written; see <see cref="NestedPlaceholderTests"/>.
/// </summary>
[Validate]
public class NestedPlaceholderModel
{
    [DisplayName("{PropertyName} total")]
    [NotEmpty]
    public string Total { get; set; } = "ok";

    [DisplayName("{MinLength} chars")]
    [MinLength(2, Message = "{PropertyName} needs {MinLength}.")]
    public string Chars { get; set; } = "ok";

    [DisplayName("{PropertyValue} label")]
    [NotEmpty]
    public string Label { get; set; } = "ok";

    [Tagged(Tag = "{PropertyName}")]
    public string? NameTag { get; set; } = "ok";

    [Tagged(Tag = "{PropertyValue}")]
    public string? ValueTag { get; set; } = "ok";

    [Tagged(Tag = "{PropertyValue}", Message = "{Tag} vs {PropertyValue}")]
    public string? MixedTag { get; set; } = "ok";

    [Equal("{PropertyValue}", Message = "{PropertyName} must equal {ComparisonValue}.")]
    public string? Compared { get; set; } = "{PropertyValue}";

    [Equal("{PropertyValue}")]
    public string? DefaultCompared { get; set; } = "{PropertyValue}";
}
