using FluentValidation;
using ZeroAlloc.Validation.Benchmarks.Models;

namespace ZeroAlloc.Validation.Benchmarks.Validators;

public sealed class FVBenchCartValidator : AbstractValidator<BenchCart>
{
    private static readonly FVBenchLineItemValidator _itemValidator = new();

    public FVBenchCartValidator()
    {
        RuleFor(x => x.CartId).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(_itemValidator);
    }
}
