using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class LengthTests
{
    private readonly LengthModelValidator _validator = new();

    [Fact]
    public void Valid_Boundary_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new LengthModel { Name = "Hi" }));
        ValidationAssert.NoErrors(_validator.Validate(new LengthModel { Name = "1234567890" }));
    }

    [Fact]
    public void TooShort_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new LengthModel { Name = "A" }), "Name");
    }

    [Fact]
    public void TooLong_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new LengthModel { Name = "12345678901" }), "Name");
    }
}
