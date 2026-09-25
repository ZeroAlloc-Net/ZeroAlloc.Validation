using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

// Models nested in other types with the same simple name, issue #207.
public static class Billing
{
    [Validate]
    public class Request
    {
        [NotEmpty] public string Account { get; set; } = "";
    }
}
