using System.Runtime.InteropServices;

namespace ZeroAlloc.Validation.AotSmoke;

/// <summary>A user struct checked by a custom rule, directly and as a nullable.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SmokeRange(int Min, int Max);
