using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

[Validate]
public class ApiKeyOptions
{
    [NotEmpty]       public string Key    { get; set; } = "";
    [GreaterThan(0)] public int    Expiry { get; set; }
}
