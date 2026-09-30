using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// [EmailAddress] and [Matches] on optional strings. All null by default, which must be valid:
/// whether a missing value is acceptable is [NotNull]'s or [NotEmpty]'s decision. An empty string
/// is a value and is still checked. See #280.
/// </summary>
[Validate]
public class NullableStringFormatModel
{
    [EmailAddress] public string? Email { get; set; }

    // Does not match the empty string, so "" fails while null passes.
    [Matches(@"^\d+$")] public string? Digits { get; set; }

    // Matches the empty string, so both null and "" pass.
    [Matches(@"^\d*$")] public string? OptionalDigits { get; set; }
}
