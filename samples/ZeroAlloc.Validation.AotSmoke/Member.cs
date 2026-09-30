using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

// An asynchronous rule between two synchronous ones, and one behind a When guard.
[Validate]
public sealed class Member
{
    [NotEmpty]
    [AvailableHandle]
    [MaxLength(12)]
    public string Handle { get; set; } = "";

    [AvailableHandle(When = nameof(HasAlias))]
    public string? Alias { get; set; }

    public bool HasAlias() => Alias is not null;
}
