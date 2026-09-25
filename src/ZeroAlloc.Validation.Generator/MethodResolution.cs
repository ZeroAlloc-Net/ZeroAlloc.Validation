using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// How <c>instance.Name(...)</c> compiles in the generated validator; see
/// <see cref="MethodCallProbe.Resolve"/>.
/// </summary>
/// <param name="Reach">Whether the call can be emitted, and if not, which diagnostic says why.</param>
/// <param name="Method">The method the verdict is about, when there is one.</param>
/// <param name="Reason">
/// For <see cref="MethodReach.NotFound"/>, the call and the compiler's error for it; the end of
/// ZV0030's message. <see langword="null"/> otherwise.
/// </param>
internal readonly record struct MethodResolution(MethodReach Reach, IMethodSymbol? Method, string? Reason)
{
    /// <summary>
    /// Whether the call is emitted: it compiles, or its member is missing from the generator's
    /// input and may come from another generator.
    /// </summary>
    public bool IsEmitted => Reach is MethodReach.Callable or MethodReach.MissingFromInput;
}
