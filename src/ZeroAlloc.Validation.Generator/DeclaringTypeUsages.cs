using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Every model's walks of one type, merged: the input of the step that reports the diagnostics
/// about the type's usages, once, issue #290.
/// </summary>
internal sealed record DeclaringTypeUsages(string Key, EquatableArray<TypeConstruction> Constructions, EquatableArray<DiagnosticInfo> Shared);
