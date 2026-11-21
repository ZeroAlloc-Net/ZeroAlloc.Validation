using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// The allocation gates count every byte the measured calls allocate, so they run alone, after the
/// parallel tests. A test running alongside can evict a runtime cache that a measured call then
/// rebuilds, and that rebuild would land in the measured window.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationCollection
{
    public const string Name = "Allocation";
}
