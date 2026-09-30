using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Has no asynchronous rule of its own; its nested and collection models do.</summary>
[Validate]
public sealed class AsyncRuleParent
{
    [NotEmpty]
    public string? Title { get; set; }

    public AsyncRuleChild? Child { get; set; }

#pragma warning disable MA0016 // deliberately concrete: pins the async walk of a List<T>, by index
    public List<AsyncRuleChild>? Children { get; set; }
#pragma warning restore MA0016

    public AsyncRuleChild[]? Others { get; set; }
}
