using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A generic model composed by <see cref="GenericPage{TItem}"/>, issue #238.</summary>
[Validate]
public class GenericLine<TItem> where TItem : class
{
    [GreaterThan(0)]
    public int Quantity { get; set; } = 1;

    [NotNull]
    public TItem? Item { get; set; }
}
