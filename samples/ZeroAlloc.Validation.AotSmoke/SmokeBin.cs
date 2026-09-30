using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

// A generic model closed over a class, issue #238, holding a closing of another generic model.
[Validate]
public sealed class SmokeBin<TItem> where TItem : class
{
    [NotEmpty] public string Label { get; set; } = "";

    [NotNull] public TItem? Item { get; set; }

    public SmokeQuantity<int> Count { get; set; } = new() { Amount = 1 };
}
