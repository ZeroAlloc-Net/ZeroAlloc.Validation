using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

// Hand-written, not generated: AddZeroAllocValidators still has to supply it, issue #246.
public sealed class DockValidator : ValidatorFor<Dock>
{
    public override ValidationResult Validate(Dock instance) =>
        instance.Number > 0
            ? new ValidationResult(System.Array.Empty<ValidationFailure>())
            : new ValidationResult(
                [new ValidationFailure { PropertyName = "Number", ErrorMessage = "Dock number must be positive." }]);
}
