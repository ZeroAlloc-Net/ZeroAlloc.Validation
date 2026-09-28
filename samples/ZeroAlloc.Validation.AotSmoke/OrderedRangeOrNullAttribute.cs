using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

/// <summary>A custom rule over a nullable user struct: null passes.</summary>
[RuleMessage("{PropertyName} must be null or have Min <= Max.")]
public sealed class OrderedRangeOrNullAttribute : ValidationAttribute<SmokeRange?>
{
    public override bool IsValid(SmokeRange? value) => value is not { } range || range.Min <= range.Max;
}
