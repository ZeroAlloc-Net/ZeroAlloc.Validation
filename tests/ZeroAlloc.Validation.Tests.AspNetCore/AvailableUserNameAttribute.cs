using System;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

/// <summary>An asynchronous rule standing in for a uniqueness lookup: a name starting with "taken" is taken.</summary>
[RuleMessage("{PropertyName} '{PropertyValue}' is taken.")]
public sealed class AvailableUserNameAttribute : AsyncValidationAttribute<string?>
{
    public override async ValueTask<bool> IsValidAsync(string? value, CancellationToken ct)
    {
        await Task.Yield();
        return value is null || !value.StartsWith("taken", StringComparison.Ordinal);
    }
}
