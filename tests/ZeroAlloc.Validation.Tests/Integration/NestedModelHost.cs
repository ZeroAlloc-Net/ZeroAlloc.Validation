using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

// [Validate] models declared inside other types, issue #207. The generated validator for a
// nested model is a top-level class in the model's namespace, named after the containing
// types joined with underscores: NestedModelHost.Request gets NestedModelHost_RequestValidator.
public sealed class NestedModelHost
{
    [Validate]
    public sealed class Request
    {
        [NotEmpty] public string? Name { get; init; }
    }

    [Validate]
    public sealed class Envelope
    {
        // A nested model whose property is a nested model in a different container.
        public NestedModelLevel1.Level2.Request Line { get; init; } = new();
    }

    // A type without [Validate], checked by a hand-written validator that is itself nested,
    // referenced through [ValidateWith].
    public sealed class Reference
    {
        public string? Code { get; init; }
    }

    public sealed class ReferenceValidator : ValidatorFor<Reference>
    {
        public override ValidationResult Validate(Reference instance) =>
            string.Equals(instance.Code, "rejected", System.StringComparison.Ordinal)
                ? new ValidationResult(new[] { new ValidationFailure { PropertyName = "Code", ErrorMessage = "Code is rejected." } })
                : new ValidationResult(System.Array.Empty<ValidationFailure>());
    }
}
