using ZeroAlloc.Pipeline;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A pass-through synchronous behavior on <see cref="AllocBehaviorFlatModel"/>.</summary>
[PipelineBehavior(Order = 0, AppliesTo = typeof(AllocBehaviorFlatModel))]
public class AllocBehaviorFlatAudit : IPipelineBehavior
{
    public static ValidationResult Handle<TModel, TState>(TModel instance, TState state, System.Func<TModel, TState, ValidationResult> next)
        => next(instance, state);
}
