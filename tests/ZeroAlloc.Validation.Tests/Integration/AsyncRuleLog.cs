using System.Collections.Generic;
using System.Threading;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Records which asynchronous rules ran, in order, for the test that is running. An
/// <see cref="AsyncLocal{T}"/> flows across the awaits, so tests running in parallel do not see
/// each other's entries.
/// </summary>
public static class AsyncRuleLog
{
    private static readonly AsyncLocal<List<string>?> Current = new();

    public static IReadOnlyList<string> Start()
    {
        var log = new List<string>();
        Current.Value = log;
        return log;
    }

    public static void Add(string entry) => Current.Value?.Add(entry);
}
