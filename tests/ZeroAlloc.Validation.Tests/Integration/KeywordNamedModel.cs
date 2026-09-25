using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

// Members named with keywords, issue #242. The generated validator must write each one with the
// @ escape wherever it reads or calls it, and report it by its name, without the escape.
[Validate]
public class KeywordNamedModel
{
    [NotEmpty(Message = "{PropertyName} was '{PropertyValue}'.")]
    public string? @class { get; set; }

    [GreaterThan(0, When = nameof(@if))]
    public int @event { get; set; }

    [Must(nameof(@is))]
    public string? @var { get; set; }

    public KeywordNamedChild? @default { get; set; }

    public IList<KeywordNamedChild> @this { get; set; } = new List<KeywordNamedChild>();

    public bool CheckEvent { get; set; } = true;

    public bool @if() => CheckEvent;

    public bool @is(string? value) => !string.Equals(value, "bad", System.StringComparison.Ordinal);

    [CustomValidation]
    public ValidationFailure[] @return() =>
        string.Equals(@class, "custom", System.StringComparison.Ordinal)
            ? new[] { new ValidationFailure { PropertyName = nameof(@return), ErrorMessage = "custom failure" } }
            : System.Array.Empty<ValidationFailure>();
}
