using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

// Action arguments nested in other types with the same simple name, issue #207.
public static class Returns
{
    public static class Inbound
    {
        [Validate]
        public class Request
        {
            [GreaterThan(0)] public int Quantity { get; set; }
        }
    }
}
