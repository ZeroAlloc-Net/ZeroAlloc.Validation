using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchCartValidator : AbstractValidator<BenchCart>
{
    public FVBenchCartValidator()
    {
        RuleFor(x => x.CartId).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(new FVBenchLineItemValidator());
    }
}
