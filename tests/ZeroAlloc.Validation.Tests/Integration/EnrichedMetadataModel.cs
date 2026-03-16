using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class EnrichedMetadataModel
{
    [NotEmpty(ErrorCode = "NAME_REQUIRED")]
    public string Name { get; set; } = "";

    [GreaterThan(0, Severity = Severity.Warning, ErrorCode = "AGE_WARN")]
    public int Age { get; set; }

    [MaxLength(100)]
    public string Bio { get; set; } = "";
}
