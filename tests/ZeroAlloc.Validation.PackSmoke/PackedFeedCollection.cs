using Xunit;

namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>
/// Shares one <see cref="PackedFeed"/> across every PackSmoke test class. As a class fixture
/// each class packed the repository on its own, and xUnit ran those packs and the consumer
/// builds in parallel, which stalled the Linux CI job indefinitely.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PackedFeedCollection : ICollectionFixture<PackedFeed>
{
    public const string Name = "PackedFeed";
}
