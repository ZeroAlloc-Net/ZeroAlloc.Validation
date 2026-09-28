using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

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

    private readonly NullableEnumModelValidator _nullableValidator = new();

    [Fact]
    public void Nullable_Null_Passes()
    {
        // A missing value is [NotNull]'s decision, the same as for the length rules.
        ValidationAssert.NoErrors(_nullableValidator.Validate(new NullableEnumModel { Light = null }));
    }

    [Fact]
    public void Nullable_DefinedValue_Passes()
    {
        ValidationAssert.NoErrors(_nullableValidator.Validate(new NullableEnumModel { Light = TrafficLight.Red }));
    }

    [Fact]
    public void Nullable_UndefinedValue_Fails()
    {
        ValidationAssert.HasError(
            _nullableValidator.Validate(new NullableEnumModel { Light = (TrafficLight)99 }), "Light");
    }
}
