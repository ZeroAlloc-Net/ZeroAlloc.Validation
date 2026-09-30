using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// A custom rule a type parameter converts to, issue #238: <c>object?</c>, so it accepts the null
/// a nullable reference-type closing may hold.
/// </summary>
[RuleMessage("{PropertyName} must not be the default value.")]
public sealed class NotDefaultAttribute : ValidationAttribute<object?>
{
    public override bool IsValid(object? value) => value is not null && !value.Equals(0);
}
