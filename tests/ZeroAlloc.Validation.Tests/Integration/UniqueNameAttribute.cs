using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// An asynchronous rule standing in for a uniqueness lookup: a value starting with "taken" is
/// taken. It yields before answering, so the validator really awaits it.
/// </summary>
[RuleMessage("{PropertyName} '{PropertyValue}' is taken.", ErrorCode = "TAKEN")]
public sealed class UniqueNameAttribute : AsyncValidationAttribute<string?>
{
    public override async ValueTask<bool> IsValidAsync(string? value, CancellationToken ct)
    {
        AsyncRuleLog.Add("UniqueName:" + value);
        await Task.Yield();
        return value is null || !value.StartsWith("taken", System.StringComparison.Ordinal);
    }
}
