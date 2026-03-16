using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
[SkipWhen(nameof(ShouldSkip))]
public class SkipWhenModel
{
    [NotEmpty]
    public string Name { get; set; } = "ok";

    public bool IsDraft { get; set; }
    internal bool ShouldSkip() => IsDraft;
}
