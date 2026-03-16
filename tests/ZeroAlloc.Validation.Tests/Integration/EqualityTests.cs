using System.Linq;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class EqualityTests
{
    private readonly EqualityModelValidator _validator = new();

    [Fact]
    public void Valid_EqualityModel_Passes()
    {
        var result = _validator.Validate(new EqualityModel { Status = "active", Score = 1.0 });
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Equal_WrongString_FailsWithCustomMessage()
    {
        var result = _validator.Validate(new EqualityModel { Status = "inactive", Score = 1.0 });
        ValidationAssert.HasError(result, "Status");
        Assert.Equal("Status must be active.", result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Status", StringComparison.Ordinal)).ErrorMessage);
    }

    [Fact]
    public void NotEqual_MatchingValue_Fails()
    {
        var result = _validator.Validate(new EqualityModel { Status = "active", Score = 0.0 });
        ValidationAssert.HasError(result, "Score");
    }
}
