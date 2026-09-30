using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// What one model's generation knows about a type it walks: how it walks it, or
/// <see langword="null"/> when it holds only shared diagnostics for it, and the diagnostics about
/// the type's usages that depend on the model, recorded by <see cref="DiagnosticSink.SharedFor"/>.
/// </summary>
internal sealed record TypeWalk(string Key, TypeConstruction? Construction, EquatableArray<DiagnosticInfo> Shared);
