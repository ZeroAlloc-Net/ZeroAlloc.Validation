using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchAddressValidator : AbstractValidator<BenchAddress>
{
    public FVBenchAddressValidator()
    {
        RuleFor(x => x.Street).NotEmpty();
        RuleFor(x => x.City).NotEmpty();
        RuleFor(x => x.PostalCode).NotEmpty();
    }
}
