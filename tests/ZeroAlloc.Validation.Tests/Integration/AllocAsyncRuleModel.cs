using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocAsyncRuleModel
{
    [NotEmpty]
    [SyncCompletingAsyncRule]
    public string? Name { get; set; }
}
