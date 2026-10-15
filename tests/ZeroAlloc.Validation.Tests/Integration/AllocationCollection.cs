using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// The allocation gates count every byte the measured calls allocate, so they run alone, after the
/// parallel tests. A test running alongside can evict a runtime cache that a measured call then
/// rebuilds. On .NET 8 an enum type keeps its <c>EnumInfo</c> in one cache slot on its
/// <see cref="System.Type"/>, which other caches such as <c>Activator.CreateInstance</c>'s take
/// over; <c>Enum.IsDefined</c> then rebuilds it. In a parallel run that put 256 B into the measured
/// window of <c>GenericEnumClosing_ValidPath_AllocatesNothing</c>. .NET 9 and later keep both.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationCollection
{
    public const string Name = "Allocation";
}
