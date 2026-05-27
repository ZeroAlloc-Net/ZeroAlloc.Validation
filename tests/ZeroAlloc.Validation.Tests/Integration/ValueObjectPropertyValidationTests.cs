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
    }

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
