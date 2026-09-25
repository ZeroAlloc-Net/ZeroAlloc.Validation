using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public sealed class NotBlankAttribute : ValidationAttribute<string?>
{
    public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
}
