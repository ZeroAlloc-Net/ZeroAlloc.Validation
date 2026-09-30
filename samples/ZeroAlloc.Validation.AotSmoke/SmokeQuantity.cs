using System.Numerics;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

// A generic model over a number, issue #238. Closed over value types, so NativeAOT compiles an
// instantiation per closing, and the comparisons are double.CreateChecked, with no boxing.
[Validate]
public sealed class SmokeQuantity<T> where T : struct, INumber<T>
{
    [GreaterThan(0)] public T Amount { get; set; }

    [InclusiveBetween(1, 10)] public T? Limit { get; set; }
}
