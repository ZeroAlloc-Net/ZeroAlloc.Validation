using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

/// <summary>A model with an asynchronous rule: its synchronous Validate throws, so the filter must await ValidateAsync.</summary>
[Validate]
public sealed class Signup
{
    [NotEmpty]
    [AvailableUserName]
    public string UserName { get; set; } = "";
}
