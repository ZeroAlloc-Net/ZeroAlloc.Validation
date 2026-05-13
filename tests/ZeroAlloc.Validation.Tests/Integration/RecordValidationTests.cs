using Xunit;
using ZeroAlloc.Validation;

#pragma warning disable MA0048 // multiple types intentionally co-located with the test class

namespace ZeroAlloc.Validation.Tests.Integration;

// Records vs classes: the generator must accept both. Positional record params
// translate to compiler-emitted properties, which the rule walker picks up via
// IPropertySymbol enumeration — identical to the class path once past the
// syntax predicate.

[Validate]
public sealed record AddressRecord(
    [property: NotEmpty(Message = "Street is required.")] string Street,
    [property: NotEmpty(Message = "City is required.")] string City);

[Validate]
public sealed record OrderRecord(
    [property: GreaterThan(0)] int CustomerId,
    [property: NotEmpty] string Sku,
    [property: GreaterThanOrEqualTo(0)] decimal UnitPrice);

public class RecordValidationTests
{
    [Fact]
    public void AddressRecord_ValidInputs_PassesValidation()
    {
        var address = new AddressRecord(Street: "Main 1", City: "Amsterdam");
        var result = new AddressRecordValidator().Validate(address);

        Assert.True(result.IsValid);
        Assert.True(result.Failures.IsEmpty);
    }

    [Fact]
    public void AddressRecord_EmptyStreet_FailsValidation()
    {
        var address = new AddressRecord(Street: "", City: "Amsterdam");
        var result = new AddressRecordValidator().Validate(address);

        Assert.False(result.IsValid);
        Assert.Equal(1, result.Failures.Length);
        Assert.Equal("Street", result.Failures[0].PropertyName);
        Assert.Equal("Street is required.", result.Failures[0].ErrorMessage);
    }

    [Fact]
    public void OrderRecord_AllRulesEvaluated()
    {
        var order = new OrderRecord(CustomerId: 0, Sku: "", UnitPrice: -1m);
        var result = new OrderRecordValidator().Validate(order);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Failures.Length);
    }

    [Fact]
    public void OrderRecord_ValidInputs_PassesValidation()
    {
        var order = new OrderRecord(CustomerId: 42, Sku: "ABC", UnitPrice: 19.99m);
        var result = new OrderRecordValidator().Validate(order);

        Assert.True(result.IsValid);
    }
}
