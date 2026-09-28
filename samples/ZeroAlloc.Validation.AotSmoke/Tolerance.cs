using System.Runtime.InteropServices;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

/// <summary>A [Validate] struct nested as a plain property, not a collection element.</summary>
[Validate]
[StructLayout(LayoutKind.Auto)]
public readonly record struct Tolerance(
    [property: InclusiveBetween(0, 1)] double Ratio);
