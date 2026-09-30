using ZeroAlloc.Pipeline;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Pass-through synchronous behaviors on every closing of <see cref="AllocGenericBehaviorModel{T}"/>.</summary>
[PipelineBehavior(Order = 0, AppliesTo = typeof(AllocGenericBehaviorModel<>))]
public class AllocGenericBehaviorAudit : IPipelineBehavior
{
    public static ValidationResult Handle<TModel>(TModel instance, System.Func<TModel, ValidationResult> next)
        => next(instance);
}
