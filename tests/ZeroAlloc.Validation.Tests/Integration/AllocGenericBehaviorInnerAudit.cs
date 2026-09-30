using ZeroAlloc.Pipeline;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// A second synchronous behavior on <see cref="AllocGenericBehaviorModel{T}"/>, so its chain has
/// two cached levels.
/// </summary>
[PipelineBehavior(Order = 1, AppliesTo = typeof(AllocGenericBehaviorModel<>))]
public class AllocGenericBehaviorInnerAudit : IPipelineBehavior
{
    public static ValidationResult Handle<TModel>(TModel instance, System.Func<TModel, ValidationResult> next)
        => next(instance);
}
