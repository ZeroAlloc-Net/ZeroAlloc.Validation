using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

// User-written ValidatorFor<Postcode> — exercises the [ValidateWith] path
// end-to-end. The generator emits a call into this validator at the
// [ValidateWith]-annotated property's evaluation site.
public sealed class PostcodeValidator : ValidatorFor<Postcode>
{
    public override ValidationResult Validate(Postcode instance)
    {
        if (string.IsNullOrEmpty(instance.Value))
        {
            return new ValidationResult(new[]
            {
                new ValidationFailure
                {
                    PropertyName = nameof(Postcode.Value),
                    ErrorMessage = "Postcode is required.",
                    ErrorCode = null,
                    Severity = Severity.Error,
                },
            });
        }
        return new ValidationResult(System.Array.Empty<ValidationFailure>());
    }
}
