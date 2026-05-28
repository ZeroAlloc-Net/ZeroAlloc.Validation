using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

[Validate]
public sealed class Letter
{
    [ValidateWith(typeof(PostcodeValidator))]
    public Postcode Postcode { get; set; } = new();
}
