using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class ConditionalTests
{
    private readonly ConditionalModelValidator _validator = new();

    [Fact]
    public void When_ConditionFalse_RuleSkipped()
    {
        // IsActive = false → [NotEmpty(When)] is skipped even though ActiveName is empty
        ValidationAssert.NoErrors(_validator.Validate(new ConditionalModel
        {
            IsActive = false,
            ActiveName = null,
            ShortName = "ab",      // would fail MinLength unless AllowShortName
            AllowShortName = true,
            BothGuard = null
        }));
    }

    [Fact]
    public void When_ConditionTrue_RuleTriggered()
    {
        ValidationAssert.HasError(_validator.Validate(new ConditionalModel
        {
            IsActive = true,
            ActiveName = null,     // fails [NotEmpty(When = "ActiveCheck")]
            ShortName = "hello",
            AllowShortName = false,
            BothGuard = "x"
        }), "ActiveName");
    }

    [Fact]
    public void Unless_ConditionTrue_RuleSkipped()
    {
        // AllowShortName = true → [MinLength(Unless)] is skipped
        ValidationAssert.NoErrors(_validator.Validate(new ConditionalModel
        {
            IsActive = false,
            ActiveName = "x",
            ShortName = "ab",      // short, but Unless condition is true so skipped
            AllowShortName = true,
            BothGuard = "x"
        }));
    }

    [Fact]
    public void Unless_ConditionFalse_RuleTriggered()
    {
        ValidationAssert.HasError(_validator.Validate(new ConditionalModel
        {
            IsActive = false,
            ActiveName = "x",
            ShortName = "ab",      // fails [MinLength(5, Unless)] when AllowShortName=false
            AllowShortName = false,
            BothGuard = "x"
        }), "ShortName");
    }

    [Fact]
    public void BothGuards_WhenFalse_RuleSkipped()
    {
        ValidationAssert.NoErrors(_validator.Validate(new ConditionalModel
        {
            IsActive = false,      // When=false → rule skipped regardless of Unless
            ActiveName = "x",
            ShortName = "hello",
            AllowShortName = false,
            BothGuard = null
        }));
    }

    [Fact]
    public void BothGuards_WhenTrueUnlessFalse_RuleTriggered()
    {
        ValidationAssert.HasError(_validator.Validate(new ConditionalModel
        {
            IsActive = true,
            ActiveName = "x",
            ShortName = "hello",
            AllowShortName = false, // Unless=false → rule active
            BothGuard = null        // fails [NotNull(When, Unless)]
        }), "BothGuard");
    }

    [Fact]
    public void BothGuards_WhenTrueUnlessTrue_RuleSkipped()
    {
        ValidationAssert.NoErrors(_validator.Validate(new ConditionalModel
        {
            IsActive = true,
            ActiveName = "x",
            ShortName = "hello",
            AllowShortName = true,  // Unless=true → rule skipped
            BothGuard = null
        }));
    }
}
