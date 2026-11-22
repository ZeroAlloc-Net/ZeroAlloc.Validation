using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Pipeline;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A pass-through asynchronous behavior on <see cref="AllocBehaviorAsyncRuleModel"/>.</summary>
[PipelineBehavior(Order = 0, AppliesTo = typeof(AllocBehaviorAsyncRuleModel))]
public class AllocBehaviorAsyncRuleAudit : IPipelineBehavior
{
    public static ValueTask<ValidationResult> Handle<TModel, TState>(
        TModel instance, TState state,
        CancellationToken ct,
        System.Func<TModel, TState, CancellationToken, ValueTask<ValidationResult>> next)
        => next(instance, state, ct);
}
