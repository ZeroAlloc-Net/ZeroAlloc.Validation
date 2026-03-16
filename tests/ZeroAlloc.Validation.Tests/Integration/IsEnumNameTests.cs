using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class IsEnumNameTests
{
    private readonly EnumNameModelValidator _validator = new();

    [Fact]
    public void ValidName_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new EnumNameModel { LightName = "Red" }));
    }

    [Fact]
    public void InvalidName_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new EnumNameModel { LightName = "Purple" }), "LightName");
    }

    [Fact]
    public void LowercaseName_Fails_CaseSensitive()
    {
        // System.Enum.IsDefined uses case-sensitive comparison for string input
        ValidationAssert.HasError(_validator.Validate(new EnumNameModel { LightName = "red" }), "LightName");
    }

    [Fact]
    public void UppercaseName_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new EnumNameModel { LightName = "RED" }), "LightName");
    }
}
