using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Validates a <see cref="GuardedRuleModel"/> as a nested model, so its guarded rules run
/// through the parent's validator, #282.
/// </summary>
[Validate]
public class GuardedRuleParent
{
    public GuardedRuleModel Child { get; set; } = new();
}
