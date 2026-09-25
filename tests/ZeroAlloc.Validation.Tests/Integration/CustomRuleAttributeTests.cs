using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class CustomRuleAttributeTests
{
    private readonly CustomRuleModelValidator _validator = new();

    private static CustomRuleModel Valid() => new() { Name = "a", Title = "one two three" };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotBlank_rejects_null_empty_and_whitespace(string? name)
    {
        var model = Valid();
        model.Name = name;
        ValidationAssert.HasErrorWithMessage(_validator.Validate(model), "Name", "Name is invalid.");
    }

    [Fact]
    public void NotBlank_accepts_text()
    {
        var model = Valid();
        model.Name = "a";
        ValidationAssert.NoErrors(_validator.Validate(model));
    }

    [Fact]
    public void MinWords_rejects_too_few_words()
    {
        var model = Valid();
        model.Title = "one two";
        ValidationAssert.HasError(_validator.Validate(model), "Title");
    }

    [Fact]
    public void MinWords_accepts_enough_words()
    {
        var model = Valid();
        model.Title = "one two three";
        ValidationAssert.NoErrors(_validator.Validate(model));
    }

    [Fact]
    public void Valid_model_has_no_failures()
    {
        var result = _validator.Validate(Valid());
        Assert.True(result.IsValid);
        Assert.Equal(0, result.Failures.Length);
    }
}
