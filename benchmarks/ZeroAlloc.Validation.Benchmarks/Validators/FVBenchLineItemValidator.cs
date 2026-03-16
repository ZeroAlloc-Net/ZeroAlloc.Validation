using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchLineItemValidator : AbstractValidator<BenchLineItem>
{
    public FVBenchLineItemValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}
