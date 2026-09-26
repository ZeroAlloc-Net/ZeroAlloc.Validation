using System.Collections.Generic;

namespace ZeroAlloc.Validation.Generator;

/// <summary>A call the generated validator makes, with the warnings the compiler reports on it.</summary>
internal sealed record MirroredCall(CallSite Site, IReadOnlyList<CallWarning> Warnings);
