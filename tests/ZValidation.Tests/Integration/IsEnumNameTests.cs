using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

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
}
