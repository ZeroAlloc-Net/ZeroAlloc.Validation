using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchOrderNestedValidator : AbstractValidator<BenchOrderNested>
{
    public FVBenchOrderNestedValidator()
    {
        RuleFor(x => x.Reference).NotEmpty();
        // SetValidator requires a non-null validator; the ! suppresses the nullable mismatch
        // between AbstractValidator<BenchAddress> and the nullable property type BenchAddress?.
        // FV will not invoke the child validator when NotNull() fails, so this is safe.
        RuleFor(x => x.ShippingAddress).NotNull()
            .SetValidator(new FVBenchAddressValidator()!);
    }
}
