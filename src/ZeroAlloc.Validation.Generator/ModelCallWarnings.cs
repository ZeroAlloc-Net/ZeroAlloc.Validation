using System.Collections.Generic;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// The warnings on the calls in one model's generated <c>Validate</c> body, by line: the
/// <c>n</c>-th entry is the <c>n</c>-th line <see cref="CallLineWriter"/> wrote that holds a
/// call. The body is emitted by the same code for the probe and for the generated file, so
/// the lines match one for one.
/// </summary>
internal sealed class ModelCallWarnings
{
    private readonly IReadOnlyList<IReadOnlyList<MirroredCall>> _lines;

    public ModelCallWarnings(IReadOnlyList<IReadOnlyList<MirroredCall>> lines) => _lines = lines;

    /// <summary>Every call that warns, in emission order.</summary>
    public IEnumerable<MirroredCall> Warned
    {
        get
        {
            foreach (var line in _lines)
            {
                foreach (var call in line)
                {
                    if (call.Warnings.Count > 0) yield return call;
                }
            }
        }
    }

    /// <summary>The distinct warning IDs on line <paramref name="ordinal"/>, in first-seen order.</summary>
    public List<string> IdsOnLine(int ordinal)
    {
        var ids = new List<string>();
        if (ordinal >= _lines.Count) return ids;
        foreach (var call in _lines[ordinal])
        {
            foreach (var warning in call.Warnings)
            {
                if (!ids.Contains(warning.Id)) ids.Add(warning.Id);
            }
        }
        return ids;
    }
}
