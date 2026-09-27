using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Collects the diagnostics found while a validator is generated, as <see cref="DiagnosticInfo"/>
/// values, in the order they are found. The output step reports them, so the generation itself
/// can run in a pipeline step whose result compares by value, issue #209.
/// </summary>
internal sealed class DiagnosticSink
{
    private readonly List<DiagnosticInfo> _diagnostics = [];

    /// <summary>Records a diagnostic at the descriptor's default severity.</summary>
    public void Report(DiagnosticDescriptor descriptor, Location? location, params object?[] messageArgs) =>
        _diagnostics.Add(DiagnosticInfo.Create(descriptor, location, descriptor.DefaultSeverity, messageArgs));

    /// <summary>Records a diagnostic at <paramref name="severity"/> instead of the descriptor's default.</summary>
    public void ReportWithSeverity(DiagnosticDescriptor descriptor, Location? location, DiagnosticSeverity severity, params object?[] messageArgs) =>
        _diagnostics.Add(DiagnosticInfo.Create(descriptor, location, severity, messageArgs));

    public EquatableArray<DiagnosticInfo> ToEquatableArray() => EquatableArray.From(_diagnostics);
}
