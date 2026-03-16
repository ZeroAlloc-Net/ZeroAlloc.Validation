using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public partial class ConditionalModel
{
    public bool IsActive { get; set; }
    public bool AllowShortName { get; set; }

    [NotEmpty(When = nameof(ActiveCheck))]
    public string? ActiveName { get; set; }

    [MinLength(5, Unless = nameof(ShortNameOk))]
    public string ShortName { get; set; } = "";

    [NotNull(When = nameof(ActiveCheck), Unless = nameof(ShortNameOk))]
    public string? BothGuard { get; set; }

    public bool ActiveCheck() => IsActive;
    public bool ShortNameOk() => AllowShortName;
}
