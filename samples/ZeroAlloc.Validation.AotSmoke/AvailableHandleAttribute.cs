using System;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

/// <summary>
/// An asynchronous custom rule standing in for a remote lookup: a handle starting with "taken" is
/// taken. It yields before answering, so the generated ValidateAsync really awaits it.
/// </summary>
[RuleMessage("{PropertyName} '{PropertyValue}' is taken.", ErrorCode = "TAKEN")]
public sealed class AvailableHandleAttribute : AsyncValidationAttribute<string?>
{
    public override async ValueTask<bool> IsValidAsync(string? value, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return value is null || !value.StartsWith("taken", StringComparison.Ordinal);
    }
}
