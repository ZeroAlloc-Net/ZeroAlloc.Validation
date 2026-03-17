using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchOrderValidator : AbstractValidator<BenchOrder>
{
    public FVBenchOrderValidator()
    {
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Amount).GreaterThan(0);
        // Note: FV uses DataAnnotations-compatible email validation; ZA uses a custom lightweight
        // validator. Both agree on "customer@example.com" (valid) and "not-an-email" (invalid),
        // so the benchmark inputs exercise equivalent code paths despite the different implementations.
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
