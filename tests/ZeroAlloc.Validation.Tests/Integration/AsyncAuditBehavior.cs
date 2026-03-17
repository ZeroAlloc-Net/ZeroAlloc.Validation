#pragma warning disable MA0069 // Non-constant static field used for [ThreadStatic] test observability
#pragma warning disable MA0016 // IList<T> abstraction — ThreadStatic field init requires concrete type at declaration
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Pipeline;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[PipelineBehavior(Order = 0, AppliesTo = typeof(PipelineOrder))]
public class AsyncAuditBehavior : IPipelineBehavior
{
    // Thread-local log for test observability
    [System.ThreadStatic]
    public static List<string>? CallLog;

    public static async ValueTask<ValidationResult> Handle<TModel>(
        TModel instance,
        CancellationToken ct,
        System.Func<TModel, CancellationToken, ValueTask<ValidationResult>> next)
    {
        CallLog?.Add("pre");
        var result = await next(instance, ct).ConfigureAwait(false);
        CallLog?.Add("post");
        return result;
    }
}
