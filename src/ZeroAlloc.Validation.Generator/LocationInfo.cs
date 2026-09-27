using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// A source location a pipeline model can hold and compare by value: the syntax tree and the span
/// in it. The tree compares by reference, and the compiler reuses the tree of a file that did not
/// change, so a model holding one still compares equal across compilations, issue #209.
/// </summary>
/// <remarks>
/// The location is rebuilt from the tree, never from its file path. A location made from a path
/// has no <see cref="Location.SourceTree"/>, so <c>#pragma warning disable</c> and per-file
/// severity would no longer apply to the diagnostic reported at it.
/// </remarks>
internal sealed record LocationInfo(SyntaxTree Tree, TextSpan Span)
{
    public Location ToLocation() => Location.Create(Tree, Span);

    /// <summary>
    /// The location's tree and span, or <see langword="null"/> for a location outside source, such
    /// as one in metadata, which the diagnostic is then reported without, at no location.
    /// </summary>
    public static LocationInfo? From(Location? location) =>
        location is { IsInSource: true, SourceTree: { } tree } ? new LocationInfo(tree, location.SourceSpan) : null;
}
