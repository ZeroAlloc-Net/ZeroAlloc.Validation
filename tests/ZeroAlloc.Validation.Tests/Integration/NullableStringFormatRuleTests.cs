using System;
using System.Linq;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// [EmailAddress] and [Matches] constrain a string that is present. A null passes them, as it
/// passes the length and comparison rules; an empty string is still checked. [EmailAddress]
/// used to reject null, and [Matches] rejected it only when the pattern did not match "". See #280.
/// </summary>
public class NullableStringFormatRuleTests
{
    private static readonly NullableStringFormatModelValidator Validator = new();

    [Fact]
    public void NullValues_Pass()
    {
        var result = Validator.Validate(new NullableStringFormatModel());

        Assert.True(result.IsValid,
            string.Join("; ", result.Failures.ToArray().Select(f => f.ErrorMessage)));
    }

    [Fact]
    public void EmptyEmail_Fails()
    {
        var result = Validator.Validate(new NullableStringFormatModel { Email = "" });

        Assert.Collection(result.Failures.ToArray(),
            f => Assert.Equal("Email must be a valid email address.", f.ErrorMessage));
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("not-an-email", false)]
    public void NonNullEmail_IsStillChecked(string email, bool valid)
    {
        var result = Validator.Validate(new NullableStringFormatModel { Email = email });

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Empty_FailsAPatternThatDoesNotMatchTheEmptyString()
    {
        var result = Validator.Validate(new NullableStringFormatModel { Digits = "" });

        Assert.Collection(result.Failures.ToArray(),
            f =>
            {
                Assert.Equal(nameof(NullableStringFormatModel.Digits), f.PropertyName);
                Assert.Equal("Digits does not match the required pattern.", f.ErrorMessage);
            });
    }

    [Fact]
    public void Empty_PassesAPatternThatMatchesTheEmptyString()
    {
        var result = Validator.Validate(new NullableStringFormatModel { OptionalDigits = "" });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("12a", false)]
    public void NonNullValue_IsStillMatched(string value, bool valid)
    {
        var result = Validator.Validate(new NullableStringFormatModel { Digits = value, OptionalDigits = value });

        Assert.Equal(valid ? 0 : 2, result.Failures.Length);
    }

    [Fact]
    public void NullValue_WithNotEmpty_ReportsOnceFromNotEmpty()
    {
        var result = new NullableStringFormatNotEmptyModelValidator()
            .Validate(new NullableStringFormatNotEmptyModel());

        Assert.Collection(result.Failures.ToArray(),
            f => Assert.Equal("Email must not be empty.", f.ErrorMessage),
            f => Assert.Equal("Digits must not be empty.", f.ErrorMessage));
    }
}
