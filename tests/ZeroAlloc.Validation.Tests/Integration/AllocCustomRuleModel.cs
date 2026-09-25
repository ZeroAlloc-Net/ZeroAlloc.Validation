using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocCustomRuleModel
{
    [NotBlank]
    public string? Name { get; set; }

    [MinWords(3)]
    public string? Title { get; set; }
}
