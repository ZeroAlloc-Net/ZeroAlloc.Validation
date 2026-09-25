using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Every placeholder in a message is substituted exactly once. A token inside a substituted
/// value, whether a display name, a custom-rule argument or a comparison value, is literal text.
/// </summary>
public class NestedPlaceholderTests
{
    private readonly NestedPlaceholderModelValidator _validator = new();

    [Fact]
    public void Valid_model_has_no_failures()
    {
        ValidationAssert.NoErrors(_validator.Validate(new NestedPlaceholderModel()));
    }

    [Fact]
    public void DisplayName_containing_PropertyName_is_literal()
    {
        var result = _validator.Validate(new NestedPlaceholderModel { Total = "" });
        ValidationAssert.HasErrorWithMessage(result, "Total", "{PropertyName} total must not be empty.");
    }

    [Fact]
    public void DisplayName_containing_a_later_placeholder_is_not_expanded()
    {
        var result = _validator.Validate(new NestedPlaceholderModel { Chars = "x" });
        ValidationAssert.HasErrorWithMessage(result, "Chars", "{MinLength} chars needs 2.");
    }

    [Fact]
    public void DisplayName_containing_PropertyValue_is_literal()
    {
        var result = _validator.Validate(new NestedPlaceholderModel { Label = "" });
        ValidationAssert.HasErrorWithMessage(result, "Label", "{PropertyValue} label must not be empty.");
    }

    [Fact]
    public void Custom_rule_argument_containing_PropertyName_is_literal()
    {
        var result = _validator.Validate(new NestedPlaceholderModel { NameTag = "bad" });
        ValidationAssert.HasErrorWithMessage(result, "NameTag", "NameTag: {PropertyName}");
    }

    [Fact]
    public void Custom_rule_argument_containing_PropertyValue_is_literal()
    {
        var result = _validator.Validate(new NestedPlaceholderModel { ValueTag = "bad" });
        ValidationAssert.HasErrorWithMessage(result, "ValueTag", "ValueTag: {PropertyValue}");
    }

    [Fact]
    public void Custom_rule_argument_containing_PropertyValue_survives_beside_the_runtime_value()
    {
        var result = _validator.Validate(new NestedPlaceholderModel { MixedTag = "bad" });
        ValidationAssert.HasErrorWithMessage(result, "MixedTag", "{PropertyValue} vs bad");
    }

    [Fact]
    public void Comparison_value_containing_PropertyValue_is_literal()
    {
        var result = _validator.Validate(new NestedPlaceholderModel { Compared = "other" });
        ValidationAssert.HasErrorWithMessage(result, "Compared", "Compared must equal {PropertyValue}.");
    }

    [Fact]
    public void Comparison_value_containing_PropertyValue_is_literal_in_default_message()
    {
        var result = _validator.Validate(new NestedPlaceholderModel { DefaultCompared = "other" });
        ValidationAssert.HasErrorWithMessage(
            result, "DefaultCompared", "DefaultCompared must equal \"{PropertyValue}\".");
    }
}
