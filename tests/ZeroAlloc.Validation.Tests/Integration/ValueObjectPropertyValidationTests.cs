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
}
