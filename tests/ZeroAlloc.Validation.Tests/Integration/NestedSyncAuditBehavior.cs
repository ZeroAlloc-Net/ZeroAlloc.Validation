#pragma warning disable MA0069 // Non-constant static field used for [ThreadStatic] test observability
#pragma warning disable MA0016 // IList<T> abstraction — ThreadStatic field init requires concrete type at declaration
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Pipeline;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A synchronous behavior on <see cref="PipelineNestedOrder"/>, issue #294.</summary>
[PipelineBehavior(Order = 0, AppliesTo = typeof(PipelineNestedOrder))]
public class NestedSyncAuditBehavior : IPipelineBehavior
{
    [System.ThreadStatic]
    public static List<string>? CallLog;

    public static ValidationResult Handle<TModel>(TModel instance, System.Func<TModel, ValidationResult> next)
    {
        CallLog?.Add("sync");
        return next(instance);
    }
}
