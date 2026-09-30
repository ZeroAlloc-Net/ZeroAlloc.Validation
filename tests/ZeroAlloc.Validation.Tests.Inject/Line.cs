using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

// Composed by Page<TItem>, and so registered for every closing of it that is registered, #238.
[Validate]
public class Line<TItem> where TItem : class
{
    [GreaterThan(0)] public int Quantity { get; set; } = 1;

    [NotNull] public TItem? Item { get; set; }
}
