using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Pipeline;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A pass-through asynchronous behavior on every closing of <see cref="AllocGenericBehaviorModel{T}"/>.</summary>
[PipelineBehavior(Order = 2, AppliesTo = typeof(AllocGenericBehaviorModel<>))]
public class AllocGenericBehaviorAsyncAudit : IPipelineBehavior
{
    public static ValueTask<ValidationResult> Handle<TModel>(
        TModel instance,
        CancellationToken ct,
        System.Func<TModel, CancellationToken, ValueTask<ValidationResult>> next)
        => next(instance, ct);
}
