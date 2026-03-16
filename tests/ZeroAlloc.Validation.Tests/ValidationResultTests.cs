using Xunit;
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests;

public class ValidationResultTests
{
    [Fact]
    public void IsValid_WhenNoFailures_ReturnsTrue()
    {
        var result = new ValidationResult([]);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void IsValid_WhenFailuresPresent_ReturnsFalse()
    {
        var failure = new ValidationFailure { PropertyName = "Name", ErrorMessage = "Required" };
        var result = new ValidationResult([failure]);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidationAssert_HasError_PassesWhenErrorPresent()
    {
        var failure = new ValidationFailure { PropertyName = "Name", ErrorMessage = "Required" };
        var result = new ValidationResult([failure]);
        ValidationAssert.HasError(result, "Name");
    }

    [Fact]
    public void ValidationAssert_HasError_ThrowsWhenErrorAbsent()
    {
        var result = new ValidationResult([]);
        Assert.Throws<ValidationAssertException>(() => ValidationAssert.HasError(result, "Name"));
    }
}
