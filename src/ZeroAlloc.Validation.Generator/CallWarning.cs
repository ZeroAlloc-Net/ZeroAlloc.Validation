namespace ZeroAlloc.Validation.Generator;

/// <summary>A compiler warning on one call, with the compiler's own ID and message.</summary>
internal readonly record struct CallWarning(string Id, string Message);
