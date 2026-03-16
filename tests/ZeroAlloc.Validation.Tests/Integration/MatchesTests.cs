using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class MatchesTests
{
    private readonly MatchesModelValidator _validator = new();

    [Fact]
    public void Valid_ZipCode_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "US" }));
    }

    [Fact]
    public void Invalid_ZipCode_Letters_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = "ABCDE", CountryCode = "US" }), "ZipCode");
    }

    [Fact]
    public void Invalid_ZipCode_TooShort_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = "1234", CountryCode = "US" }), "ZipCode");
    }

    [Fact]
    public void Invalid_ZipCode_ReportsCustomMessage()
    {
        var result = _validator.Validate(new MatchesModel { ZipCode = "bad", CountryCode = "US" });
        var failure = System.Linq.Enumerable.First(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "ZipCode", System.StringComparison.Ordinal));
        Assert.Equal("ZipCode must be exactly 5 digits.", failure.ErrorMessage);
    }

    [Fact]
    public void Valid_CountryCode_TwoLetter_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "US" }));
    }

    [Fact]
    public void Valid_CountryCode_ThreeLetter_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "USA" }));
    }

    [Fact]
    public void Invalid_CountryCode_Lowercase_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "us" }), "CountryCode");
    }

    [Fact]
    public void Invalid_CountryCode_TooLong_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = "12345", CountryCode = "USAA" }), "CountryCode");
    }

    [Fact]
    public void Null_Value_FailsMatches()
    {
        // Generator emits: !Regex.IsMatch(access ?? "", pattern) — null is treated as empty string
        ValidationAssert.HasError(_validator.Validate(new MatchesModel { ZipCode = null!, CountryCode = "US" }), "ZipCode");
    }
}
