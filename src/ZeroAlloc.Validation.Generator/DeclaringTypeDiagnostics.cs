using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>The diagnostics the step for one declaring type reports.</summary>
internal sealed record DeclaringTypeDiagnostics(string Key, EquatableArray<DiagnosticInfo> Diagnostics);
