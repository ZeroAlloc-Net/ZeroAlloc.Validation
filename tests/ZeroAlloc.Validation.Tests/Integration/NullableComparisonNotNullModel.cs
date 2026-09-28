using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>[NotNull] paired with a comparison rule: the null is reported once, by [NotNull].</summary>
[Validate]
public class NullableComparisonNotNullModel
{
    [NotNull]
    [GreaterThan(0)]
    public int? Quantity { get; set; }

    [NotNull]
    [InclusiveBetween(1, 10)]
    public decimal? Rating { get; set; }
}
