using System.Linq;
using Xunit;
using ZeroAlloc.Validation.Internal;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// <c>Unit = TextLengthUnit.CodePoints</c> counts a character outside the Basic Multilingual Plane,
/// such as an emoji, as one, where the default counts its two UTF-16 code units, #361. A lone
/// surrogate is not part of a pair, so it counts as one code point of its own.
/// </summary>
public class CodePointLengthTests
{
    private const string Smile = "\U0001F600";
    private const string High = "\uD83D";
    private const string Low = "\uDE00";

    private readonly CodePointLengthModelValidator _validator = new();

    /// <summary>Text, built here rather than in attribute arguments, which cannot carry a lone surrogate.</summary>
    public static TheoryData<string, int> CodePoints() => new()
    {
        { "", 0 },
        { "a", 1 },
        { "abc", 3 },
        { "é", 1 },
        { "日本語", 3 },
        { Smile, 1 },
        { Smile + Smile, 2 },
        { "a" + Smile, 2 },
        { Smile + "a" + Smile, 3 },
        { High, 1 },
        { Low, 1 },
        { Low + High, 2 },
        { High + "a", 2 },
        { "a" + High, 2 },
        { High + High + Low, 2 },
        { High + Low + Low, 2 },
        { new string('a', 40) + Smile + new string('b', 40), 81 },
        { string.Concat(Enumerable.Repeat(Smile, 50)), 50 },
        { string.Concat(Enumerable.Repeat(High, 50)), 50 },
    };

    [Theory]
    [MemberData(nameof(CodePoints), DisableDiscoveryEnumeration = true)]
    public void CountCodePoints_CountsPairsOnceAndLoneSurrogatesOnce(string text, int expected)
    {
        Assert.Equal(expected, TextLength.CountCodePoints(text));
        Assert.Equal(System.Text.Encoding.UTF32.GetByteCount(text) / 4, TextLength.CountCodePoints(text));
    }

    public static TheoryData<string, bool> MaxCases() => new()
    {
        { "ab", true },
        { Smile, true },
        { Smile + Smile, true },
        { "a" + Smile, true },
        { High + Low + High, true },
        { Smile + High + High, false },
        { High + High, true },
        { "abc", false },
        { Smile + Smile + Smile, false },
        { Smile + Smile + "a", false },
    };

    [Theory]
    [MemberData(nameof(MaxCases), DisableDiscoveryEnumeration = true)]
    public void MaxLength_CountsCodePoints(string text, bool valid) =>
        AssertValid(new CodePointLengthModel { Max = text }, nameof(CodePointLengthModel.Max), valid);

    public static TheoryData<string, bool> MinCases() => new()
    {
        { "", false },
        { "a", false },
        { Smile, false },
        { High + Low, false },
        { High + High, true },
        { Smile + Smile, true },
        { "a" + Smile, true },
        { "ab", true },
        { Smile + "a" + Smile, true },
    };

    [Theory]
    [MemberData(nameof(MinCases), DisableDiscoveryEnumeration = true)]
    public void MinLength_CountsCodePoints(string text, bool valid) =>
        AssertValid(new CodePointLengthModel { Min = text }, nameof(CodePointLengthModel.Min), valid);

    public static TheoryData<string, bool> RangeCases() => new()
    {
        { "a", false },
        { Smile, false },
        { Smile + Smile, true },
        { Smile + Smile + Smile, true },
        { "ab", true },
        { "abc", true },
        { "abcd", false },
        { Smile + Smile + Smile + Smile, false },
        { Smile + Smile + Smile + "a", false },
        { High + High + High, true },
    };

    [Theory]
    [MemberData(nameof(RangeCases), DisableDiscoveryEnumeration = true)]
    public void Length_CountsCodePoints(string text, bool valid) =>
        AssertValid(new CodePointLengthModel { Range = text }, nameof(CodePointLengthModel.Range), valid);

    [Fact]
    public void IssueRepro_TwoEmojiPassACodePointMaxLength_ButNotTheDefault()
    {
        var result = _validator.Validate(new CodePointLengthModel { Max = Smile + Smile, MaxCodeUnits = Smile + Smile });

        Assert.Equal(1, result.Failures.Length);
        Assert.Equal(nameof(CodePointLengthModel.MaxCodeUnits), result.Failures[0].PropertyName);
        Assert.Equal("MaxCodeUnits must not exceed 2 characters.", result.Failures[0].ErrorMessage);
    }

    [Fact]
    public void Null_PassesEveryLengthRule() =>
        ValidationAssert.NoErrors(_validator.Validate(new CodePointLengthModel()));

    private void AssertValid(CodePointLengthModel model, string property, bool valid)
    {
        var result = _validator.Validate(model);
        if (valid)
            ValidationAssert.NoErrors(result);
        else
            ValidationAssert.HasError(result, property);
    }
}
