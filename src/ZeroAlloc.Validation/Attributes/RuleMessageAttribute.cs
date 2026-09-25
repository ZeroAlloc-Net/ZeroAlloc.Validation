namespace ZeroAlloc.Validation;

/// <summary>
/// Declares the default failure message, and optionally the error code, for a custom rule
/// attribute deriving from <see cref="ValidationAttribute{T}"/>. A <c>Message</c> or
/// <c>ErrorCode</c> set on the usage wins, including an explicit <c>ErrorCode = null</c>.
/// <c>{PropertyName}</c>, and <c>{name}</c> for any constructor parameter or named property
/// written on the usage, are resolved at compile time. <c>{PropertyValue}</c> is formatted at
/// validation time, and only when the rule fails.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RuleMessageAttribute(string message) : Attribute
{
    /// <summary>
    /// The default failure message for the rule, used when the usage sets no <c>Message</c>.
    /// It takes the same placeholders as a usage <c>Message</c>.
    /// </summary>
    public string Message { get; } = message;

    /// <summary>
    /// The default error code for the rule, used when the usage sets no <c>ErrorCode</c>. An
    /// explicit <c>ErrorCode = null</c> on the usage clears it.
    /// </summary>
    public string? ErrorCode { get; set; }
}
