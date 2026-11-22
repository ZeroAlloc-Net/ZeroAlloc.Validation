using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A model with no nested validator that a synchronous behavior applies to, issue #410.</summary>
[Validate]
public class AllocBehaviorFlatModel
{
    [NotEmpty]
    public string Reference { get; set; } = "";
}
