using System;
using System.Linq;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// A <c>[CustomValidation]</c> method may return <c>IEnumerable&lt;ValidationFailure&gt;</c>,
/// <c>ValidationFailure[]</c> or <c>ReadOnlySpan&lt;ValidationFailure&gt;</c>. Only the last two
/// can avoid allocating: an iterator allocates its state machine when it is called, before it has
/// yielded anything, so a model that is valid still pays for it.
/// </summary>
[Collection(AllocationCollection.Name)]
public class CustomValidationReturnTypeTests
{
    private const int Iterations = 10_000;

    [Fact]
    public void ArrayReturn_ReportsFailure()
    {
        var result = new CustomValidationArrayModelValidator()
            .Validate(new CustomValidationArrayModel { Reference = "ok", Budget = -1 });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Budget", StringComparison.Ordinal));
    }

    [Fact]
    public void ArrayReturn_EmptyIsValid()
    {
        var result = new CustomValidationArrayModelValidator()
            .Validate(new CustomValidationArrayModel { Reference = "ok", Budget = 5 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ArrayReturn_RunsAlongsideRuleAttributes()
    {
        var result = new CustomValidationArrayModelValidator()
            .Validate(new CustomValidationArrayModel { Reference = "", Budget = -1 });

        var names = result.Failures.ToArray().Select(f => f.PropertyName).ToArray();
        Assert.Contains("Reference", names, StringComparer.Ordinal);
        Assert.Contains("Budget", names, StringComparer.Ordinal);
    }

    [Fact]
    public void SpanReturn_ReportsFailure()
    {
        var result = new CustomValidationSpanModelValidator()
            .Validate(new CustomValidationSpanModel { Reference = "ok", Budget = -1 });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Budget", StringComparison.Ordinal));
    }

    [Fact]
    public void SpanReturn_EmptyIsValid()
    {
        var result = new CustomValidationSpanModelValidator()
            .Validate(new CustomValidationSpanModel { Reference = "ok", Budget = 5 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EnumerableReturn_StillSupported()
    {
        // The original signature keeps working; only its allocation behaviour differs.
        var result = new CustomValidationModelValidator()
            .Validate(new CustomValidationModel { Reference = "ok", RequiresPromo = true, PromoCode = null });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "PromoCode", StringComparison.Ordinal));
    }

    [Fact]
    public void ArrayReturn_ValidPath_AllocatesNothing()
    {
        var validator = new CustomValidationArrayModelValidator();
        var model = new CustomValidationArrayModel { Reference = "ok", Budget = 5 };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "ArrayReturn_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void SpanReturn_ValidPath_AllocatesNothing()
    {
        var validator = new CustomValidationSpanModelValidator();
        var model = new CustomValidationSpanModel { Reference = "ok", Budget = 5 };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "SpanReturn_ValidPath_AllocatesNothing");
    }
}
