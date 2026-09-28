using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

/// <summary>A custom rule over an enum.</summary>
[RuleMessage("{PropertyName} must be a known level.")]
public sealed class KnownLevelAttribute : ValidationAttribute<SmokeLevel>
{
    public override bool IsValid(SmokeLevel value) => value is >= SmokeLevel.Low and <= SmokeLevel.High;
}
