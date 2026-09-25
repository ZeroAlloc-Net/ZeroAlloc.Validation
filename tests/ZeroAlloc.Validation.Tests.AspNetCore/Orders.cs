using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

// Action arguments nested in other types with the same simple name, issue #207.
public static class Orders
{
    [Validate]
    public class Request
    {
        [NotEmpty] public string Name { get; set; } = "";
    }
}
