using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchPaymentValidator : AbstractValidator<BenchPayment>
{
    public FVBenchPaymentValidator()
    {
        // ignoreTrailingZeros: false matches ZeroAlloc, which counts the scale the value carries.
        RuleFor(x => x.Amount).PrecisionScale(9, 2, ignoreTrailingZeros: false);
        RuleFor(x => x.Status).IsInEnum();
    }
}
