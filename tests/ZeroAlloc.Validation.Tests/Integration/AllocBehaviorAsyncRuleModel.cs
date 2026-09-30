using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// An asynchronous rule, a nested validator and an asynchronous behavior: the chain around the
/// instance-bound async body caches its delegates, issue #298.
/// </summary>
[Validate]
public class AllocBehaviorAsyncRuleModel
{
    [NotEmpty]
    [SyncCompletingAsyncRule]
    public string? Name { get; set; }

    public PipelineNestedLine? Line { get; set; }
}
