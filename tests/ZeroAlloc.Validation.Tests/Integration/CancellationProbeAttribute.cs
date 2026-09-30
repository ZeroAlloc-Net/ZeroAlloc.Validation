using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>An asynchronous rule that observes the token it is given and passes otherwise.</summary>
public sealed class CancellationProbeAttribute : AsyncValidationAttribute<string?>
{
    public override async ValueTask<bool> IsValidAsync(string? value, CancellationToken ct)
    {
        await Task.Delay(1, ct).ConfigureAwait(false);
        return true;
    }
}
