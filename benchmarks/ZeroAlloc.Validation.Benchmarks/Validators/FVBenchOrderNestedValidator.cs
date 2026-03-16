using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchOrderNestedValidator : AbstractValidator<BenchOrderNested>
{
    public FVBenchOrderNestedValidator()
    {
        RuleFor(x => x.Reference).NotEmpty();
        RuleFor(x => x.ShippingAddress).NotNull()
            .SetValidator(new FVBenchAddressValidator()!);
    }
}
