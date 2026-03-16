using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class SkipWhenTests
{
    private readonly SkipWhenModelValidator _validator = new();

    [Fact]
    public void SkipWhen_True_ReturnsValidResult()
    {
        var model = new SkipWhenModel { Name = "", IsDraft = true };
        var result = _validator.Validate(model);
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void SkipWhen_False_RunsNormalValidation()
    {
        var model = new SkipWhenModel { Name = "", IsDraft = false };
        var result = _validator.Validate(model);
        ValidationAssert.HasError(result, "Name");
    }

    [Fact]
    public void SkipWhen_False_AllPass_NoErrors()
    {
        var model = new SkipWhenModel { Name = "ok", IsDraft = false };
        var result = _validator.Validate(model);
        ValidationAssert.NoErrors(result);
    }
}
