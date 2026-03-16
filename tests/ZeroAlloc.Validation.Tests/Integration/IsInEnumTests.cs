using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class IsInEnumTests
{
    private readonly EnumModelValidator _validator = new();

    [Fact]
    public void DefinedValue_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new EnumModel { Light = TrafficLight.Green }));
    }

    [Fact]
    public void UndefinedValue_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new EnumModel { Light = (TrafficLight)99 }), "Light");
    }
}
