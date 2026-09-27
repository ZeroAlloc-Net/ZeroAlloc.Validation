using System.Globalization;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// A diagnostic as a pipeline model holds it until the output step reports it. A
/// <see cref="Diagnostic"/> does not compare by value, so a model holding one would never compare
/// equal across compilations, issue #209. The message arguments are strings for the same reason:
/// an <c>object[]</c> compares by reference.
/// </summary>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    DiagnosticSeverity Severity,
    EquatableArray<string> MessageArgs)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, DiagnosticSeverity severity, object?[] messageArgs)
    {
        var args = new string[messageArgs.Length];
        for (var i = 0; i < args.Length; i++)
            args[i] = messageArgs[i] as string ?? Convert.ToString(messageArgs[i], CultureInfo.InvariantCulture) ?? string.Empty;
        return new DiagnosticInfo(descriptor, LocationInfo.From(location), severity, new EquatableArray<string>([.. args]));
    }

    public Diagnostic ToDiagnostic()
    {
        var args = new object[MessageArgs.Count];
        for (var i = 0; i < args.Length; i++)
            args[i] = MessageArgs[i];
        return Diagnostic.Create(
            Descriptor,
            Location?.ToLocation(),
            Severity,
            additionalLocations: null,
            properties: null,
            args);
    }
}
