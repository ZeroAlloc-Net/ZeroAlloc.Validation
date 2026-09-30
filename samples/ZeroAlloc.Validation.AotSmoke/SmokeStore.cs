using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

// A non-generic model composing closings of generic models, issue #238.
[Validate]
public sealed class SmokeStore
{
    public SmokeBin<Address> Bin { get; set; } = new();

    public SmokeQuantity<long> Stock { get; set; } = new() { Amount = 1 };
}
