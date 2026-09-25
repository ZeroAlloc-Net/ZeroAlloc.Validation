using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

// Issue #207: a [Validate] model two levels deep, with the same simple name as
// NestedModelHost.Request. Its validator is NestedModelLevel1_Level2_RequestValidator.
public static class NestedModelLevel1
{
    public sealed class Level2
    {
        [Validate]
        public sealed class Request
        {
            [GreaterThan(0)] public int Quantity { get; init; }
        }
    }
}
