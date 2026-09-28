using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

/// <summary>A custom rule over a user struct.</summary>
[RuleMessage("{PropertyName} must have Min <= Max.")]
public sealed class OrderedRangeAttribute : ValidationAttribute<SmokeRange>
{
    public override bool IsValid(SmokeRange value) => value.Min <= value.Max;
}
