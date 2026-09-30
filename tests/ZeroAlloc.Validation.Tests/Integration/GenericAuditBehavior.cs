#pragma warning disable MA0069 // Non-constant static field used for [ThreadStatic] test observability
#pragma warning disable MA0016 // IList<T> abstraction — ThreadStatic field init requires concrete type at declaration
using System.Collections.Generic;
using ZeroAlloc.Pipeline;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A synchronous behavior on every closing of <see cref="GenericAudited{T}"/>, issue #238.</summary>
[PipelineBehavior(Order = 0, AppliesTo = typeof(GenericAudited<>))]
public class GenericAuditBehavior : IPipelineBehavior
{
    [System.ThreadStatic]
    public static List<string>? CallLog;

    public static ValidationResult Handle<TModel>(TModel instance, System.Func<TModel, ValidationResult> next)
    {
        CallLog?.Add(typeof(TModel).Name);
        return next(instance);
    }
}
