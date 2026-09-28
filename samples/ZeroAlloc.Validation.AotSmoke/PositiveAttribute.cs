using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

/// <summary>A custom rule over int?: null passes, a present value must be positive.</summary>
[RuleMessage("{PropertyName} must be positive, got {PropertyValue}.", ErrorCode = "POSITIVE")]
public sealed class PositiveAttribute : ValidationAttribute<int?>
{
    public override bool IsValid(int? value) => value is null || value.Value > 0;
}
