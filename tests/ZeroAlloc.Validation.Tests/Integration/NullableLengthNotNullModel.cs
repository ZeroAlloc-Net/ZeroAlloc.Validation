using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>[NotNull] paired with a length rule: the null is reported once, by [NotNull].</summary>
[Validate]
public class NullableLengthNotNullModel
{
    [NotNull]
    [MinLength(3)]
    public string? Tenant { get; set; }
}
