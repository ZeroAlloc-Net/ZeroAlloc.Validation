#pragma warning disable MA0048 // multiple types intentionally co-located

using Xunit;
using ZeroAlloc.Validation;

// Local ValueObjectAttribute matching the ZA.ValueObjects FQN — no runtime ref needed.
namespace ZeroAlloc.ValueObjects
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}

namespace ZeroAlloc.Validation.Tests.Integration
{
    public class ValueObjectPropertyValidationTests
    {
        [Fact]
        public void TypedId_GreaterThan_HappyPath_ReportsValid()
        {
            var validator = new PlaceOrderTypedCommandValidator();
            var result = validator.Validate(new PlaceOrderTypedCommand(new CustomerId(42)));
            Assert.True(result.IsValid);
            Assert.True(result.Failures.IsEmpty);
        }

        [Fact]
        public void TypedId_GreaterThan_SadPath_ReportsFailureOnTypedIdProperty()
        {
            var validator = new PlaceOrderTypedCommandValidator();
            var result = validator.Validate(new PlaceOrderTypedCommand(new CustomerId(0)));
            Assert.False(result.IsValid);
            Assert.Equal(1, result.Failures.Length);
            Assert.Equal(nameof(PlaceOrderTypedCommand.CustomerId), result.Failures[0].PropertyName);
        }

        [Theory]
        [InlineData("alice", true)]
        [InlineData("", false)]
        public void StringValueObject_NotEmpty_Behaves(string raw, bool expectedValid)
        {
            var validator = new CreateUserCommandValidator();
            var result = validator.Validate(new CreateUserCommand(new Username(raw)));
            Assert.Equal(expectedValid, result.IsValid);
        }

        [Theory]
        [InlineData(50, true)]
        [InlineData(0, false)]
        [InlineData(101, false)]
        public void TypedId_InclusiveBetween_Behaves(int raw, bool expectedValid)
        {
            var validator = new GetPageCommandValidator();
            var result = validator.Validate(new GetPageCommand(new PageNumber(raw)));
            Assert.Equal(expectedValid, result.IsValid);
        }

        // [IsInEnum] reads the unwrapped member, so Enum.IsDefined must be asked about the enum,
        // not the wrapper, which threw ArgumentException on every validation, #300.
        [Theory]
        [InlineData(PaintColor.Red, true)]
        [InlineData((PaintColor)42, false)]
        public void EnumValueObject_IsInEnum_Behaves(PaintColor raw, bool expectedValid)
        {
            var validator = new PaintCommandValidator();
            var result = validator.Validate(new PaintCommand(new Hue(raw)));
            Assert.Equal(expectedValid, result.IsValid);
            if (!expectedValid)
                Assert.Equal(nameof(PaintCommand.Tint), result.Failures[0].PropertyName);
        }

        // A value object over a nullable enum: a missing value passes, as [IsInEnum] on a
        // Nullable<TEnum> property does, and a present one is checked.
        [Theory]
        [InlineData(null, true)]
        [InlineData(PaintColor.Green, true)]
        [InlineData((PaintColor)42, false)]
        public void NullableEnumValueObject_IsInEnum_Behaves(PaintColor? raw, bool expectedValid)
        {
            var validator = new PrimeCommandValidator();
            var result = validator.Validate(new PrimeCommand(new OptionalHue(raw)));
            Assert.Equal(expectedValid, result.IsValid);
        }
    }

    public enum PaintColor { Red, Green }

    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct Hue
    {
        public PaintColor Value { get; }
        public Hue(PaintColor value) => Value = value;
    }

    [Validate]
    public readonly record struct PaintCommand(
        [property: IsInEnum] Hue Tint);

    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct OptionalHue
    {
        public PaintColor? Value { get; }
        public OptionalHue(PaintColor? value) => Value = value;
    }

    [Validate]
    public readonly record struct PrimeCommand(
        [property: IsInEnum] OptionalHue Tint);

    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct CustomerId
    {
        public int Value { get; }
        public CustomerId(int value) => Value = value;
    }

    [Validate]
    public readonly record struct PlaceOrderTypedCommand(
        [property: GreaterThan(0)] CustomerId CustomerId);

    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct Username
    {
        public string Value { get; }
        public Username(string value) => Value = value;
    }

    [Validate]
    public readonly record struct CreateUserCommand(
        [property: NotEmpty] Username Name);

    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct PageNumber
    {
        public int Value { get; }
        public PageNumber(int value) => Value = value;
    }

    [Validate]
    public readonly record struct GetPageCommand(
        [property: InclusiveBetween(1, 100)] PageNumber Page);
}
