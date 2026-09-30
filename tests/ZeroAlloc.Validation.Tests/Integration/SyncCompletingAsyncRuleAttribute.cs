using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>An asynchronous rule whose answer is ready at once, as a cache hit would be.</summary>
public sealed class SyncCompletingAsyncRuleAttribute : AsyncValidationAttribute<string?>
{
    public override ValueTask<bool> IsValidAsync(string? value, CancellationToken ct) =>
        new(!string.IsNullOrEmpty(value));
}
