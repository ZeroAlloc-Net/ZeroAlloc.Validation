using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Writes the lines of a generated <c>Validate</c> body that hold rule calls. For the probe it
/// records where each call is, so <see cref="MethodCallProbe"/> can map the compiler's
/// warnings back to it. For the generated file it wraps a line whose calls warned in a
/// <c>#pragma warning disable</c> and <c>restore</c> for exactly those warning IDs, which
/// ZV0032 reports at the attribute instead. A line that did not warn gets no pragma.
/// </summary>
internal sealed class CallLineWriter
{
    private readonly ModelCallWarnings? _warnings;
    private readonly List<List<(TextSpan Span, CallSite Site)>>? _recorded;
    private int _ordinal;

    private CallLineWriter(ModelCallWarnings? warnings, List<List<(TextSpan Span, CallSite Site)>>? recorded)
    {
        _warnings = warnings;
        _recorded = recorded;
    }

    /// <summary>Writes the generated file, with a pragma on each line <paramref name="warnings"/> says warned.</summary>
    public static CallLineWriter Emitting(ModelCallWarnings? warnings) => new(warnings, null);

    /// <summary>Writes the probe, recording each call's span in the probe text.</summary>
    public static CallLineWriter Recording() => new(null, new List<List<(TextSpan Span, CallSite Site)>>());

    /// <summary>The recorded lines, each with its calls' spans; only for <see cref="Recording"/>.</summary>
    public IReadOnlyList<List<(TextSpan Span, CallSite Site)>> Recorded => _recorded!;

    /// <summary>
    /// Appends <paramref name="line"/>, which holds <paramref name="calls"/> in the order they
    /// appear in it. A line without calls is appended as it is and not counted.
    /// </summary>
    public void AppendLine(StringBuilder sb, string line, IReadOnlyList<CallSite> calls)
    {
        if (calls.Count == 0)
        {
            sb.AppendLine(line);
            return;
        }

        int ordinal = _ordinal++;
        if (_recorded is not null)
        {
            var sites = new List<(TextSpan Span, CallSite Site)>(calls.Count);
            int from = 0;
            foreach (var call in calls)
            {
                int at = line.IndexOf(call.Text, from, System.StringComparison.Ordinal);
                System.Diagnostics.Debug.Assert(at >= 0, "Every call is written on the line it is recorded for.");
                if (at < 0) continue;
                sites.Add((new TextSpan(sb.Length + at, call.Text.Length), call));
                from = at + call.Text.Length;
            }
            _recorded.Add(sites);
            sb.AppendLine(line);
            return;
        }

        var ids = _warnings?.IdsOnLine(ordinal);
        if (ids is null || ids.Count == 0)
        {
            sb.AppendLine(line);
            return;
        }

        var list = string.Join(", ", ids);
        sb.AppendLine($"#pragma warning disable {list} // mirrored as ZV0032 at the attribute this call is made for");
        sb.AppendLine(line);
        sb.AppendLine($"#pragma warning restore {list}");
    }
}
