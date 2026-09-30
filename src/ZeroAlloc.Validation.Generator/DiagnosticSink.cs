using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Collects the diagnostics found while a validator is generated, as <see cref="DiagnosticInfo"/>
/// values, in the order they are found. The output step reports them, so the generation itself
/// can run in a pipeline step whose result compares by value, issue #209.
/// </summary>
/// <remarks>
/// A diagnostic about a usage declared on another type than the model, which no
/// <c>[Validate]</c> base type reports, goes through the sink <see cref="SharedFor"/> returns. The
/// generation of every model that walks that type may find it, so it is kept apart, under the
/// declaring type's key, and reported by that type's step, once, issue #290.
/// </remarks>
internal sealed class DiagnosticSink
{
    private readonly List<DiagnosticInfo> _diagnostics;
    private readonly List<(string TypeKey, DiagnosticInfo Diagnostic)> _shared;
    private readonly string? _typeKey;

    public DiagnosticSink()
        : this([], [], typeKey: null)
    {
    }

    private DiagnosticSink(List<DiagnosticInfo> diagnostics, List<(string TypeKey, DiagnosticInfo Diagnostic)> shared, string? typeKey)
    {
        _diagnostics = diagnostics;
        _shared = shared;
        _typeKey = typeKey;
    }

    /// <summary>Records a diagnostic at the descriptor's default severity.</summary>
    public void Report(DiagnosticDescriptor descriptor, Location? location, params object?[] messageArgs) =>
        Add(DiagnosticInfo.Create(descriptor, location, descriptor.DefaultSeverity, messageArgs));

    /// <summary>Records a diagnostic at <paramref name="severity"/> instead of the descriptor's default.</summary>
    public void ReportWithSeverity(DiagnosticDescriptor descriptor, Location? location, DiagnosticSeverity severity, params object?[] messageArgs) =>
        Add(DiagnosticInfo.Create(descriptor, location, severity, messageArgs));

    /// <summary>
    /// A sink that records into this one's diagnostics shared with the other models that walk the
    /// type <paramref name="typeKey"/> names, as <see cref="DeclaringTypes.Key"/> builds it.
    /// </summary>
    public DiagnosticSink SharedFor(string typeKey) => new(_diagnostics, _shared, typeKey);

    public EquatableArray<DiagnosticInfo> ToEquatableArray() => EquatableArray.From(_diagnostics);

    /// <summary>The shared diagnostics recorded under <paramref name="typeKey"/>, in the order they were found.</summary>
    public EquatableArray<DiagnosticInfo> SharedWith(string typeKey)
    {
        var found = new List<DiagnosticInfo>();
        foreach (var (key, diagnostic) in _shared)
        {
            if (string.Equals(key, typeKey, StringComparison.Ordinal))
                found.Add(diagnostic);
        }
        return EquatableArray.From(found);
    }

    /// <summary>The keys of the types this sink holds shared diagnostics for.</summary>
    public IEnumerable<string> SharedTypeKeys()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in _shared)
        {
            if (seen.Add(key))
                yield return key;
        }
    }

    private void Add(DiagnosticInfo diagnostic)
    {
        if (_typeKey is null)
            _diagnostics.Add(diagnostic);
        else
            _shared.Add((_typeKey, diagnostic));
    }
}
