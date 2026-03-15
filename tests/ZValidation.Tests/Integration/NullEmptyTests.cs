using System.Linq;
using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class NullEmptyTests
{
    private readonly NullEmptyModelValidator _validator = new();

    [Fact]
    public void Null_WhenNull_Passes()
    {
        var result = _validator.Validate(new NullEmptyModel { MustBeNull = null, MustBeEmpty = "" });
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Null_WhenNotNull_Fails()
    {
        var result = _validator.Validate(new NullEmptyModel { MustBeNull = "oops", MustBeEmpty = "" });
        ValidationAssert.HasError(result, "MustBeNull");
    }

    [Fact]
    public void Empty_WhenNotEmpty_FailsWithCustomMessage()
    {
        var result = _validator.Validate(new NullEmptyModel { MustBeNull = null, MustBeEmpty = "x" });
        ValidationAssert.HasError(result, "MustBeEmpty");
        Assert.Equal("Must be empty.", result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "MustBeEmpty", StringComparison.Ordinal)).ErrorMessage);
    }
}
