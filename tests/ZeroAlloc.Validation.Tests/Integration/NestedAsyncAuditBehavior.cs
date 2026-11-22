#pragma warning disable MA0069 // Non-constant static field used for [ThreadStatic] test observability
#pragma warning disable MA0016 // IList<T> abstraction — ThreadStatic field init requires concrete type at declaration
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Pipeline;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>An asynchronous behavior on <see cref="PipelineNestedOrder"/>, issue #294.</summary>
[PipelineBehavior(Order = 1, AppliesTo = typeof(PipelineNestedOrder))]
public class NestedAsyncAuditBehavior : IPipelineBehavior
{
    [System.ThreadStatic]
    public static List<string>? CallLog;

    public static async ValueTask<ValidationResult> Handle<TModel, TState>(
        TModel instance, TState state,
        CancellationToken ct,
        System.Func<TModel, TState, CancellationToken, ValueTask<ValidationResult>> next)
    {
        CallLog?.Add("async");
        return await next(instance, state, ct).ConfigureAwait(false);
    }
}
