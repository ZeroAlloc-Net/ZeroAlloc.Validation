using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public sealed class CustomRuleModel
{
    [NotBlank] public string? Name { get; set; }
    [MinWords(3)] public string? Title { get; set; }
}
