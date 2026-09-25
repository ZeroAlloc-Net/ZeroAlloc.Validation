using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A custom rule whose message echoes a named argument, used by the placeholder tests.</summary>
[RuleMessage("{PropertyName}: {Tag}")]
public sealed class TaggedAttribute : ValidationAttribute<string?>
{
    public string? Tag { get; set; }

    public override bool IsValid(string? value) => !string.Equals(value, "bad", System.StringComparison.Ordinal);
}
