using System.Linq;
using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class NestedValidationTests
{
    private readonly OrderValidator _validator = new(new AddressValidator(), new AddressValidator());

    [Fact]
    public void Valid_Order_PassesValidation()
    {
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
            BillingAddress = new Address { Street = "456 Oak Ave", City = "Shelbyville" }
        };
        var result = _validator.Validate(order);
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Nested_BillingAddress_Invalid_ReportsDotPrefixedFailure()
    {
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
            BillingAddress = new Address { Street = "", City = "Shelbyville" }
        };
        var result = _validator.Validate(order);
        var failures = result.Failures.ToArray();
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "BillingAddress.Street");
        Assert.Equal("Street is required.", failures.First(f => string.Equals(f.PropertyName, "BillingAddress.Street", System.StringComparison.Ordinal)).ErrorMessage);
    }

    [Fact]
    public void Nested_ShippingAddress_Null_SkipsNestedValidation()
    {
        // BillingAddress has default value (new Address()), so make it invalid differently
        // This test verifies null skipping by using a nullable ShippingAddress set to null
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = null,  // [NotNull] should fire
            BillingAddress = new Address { Street = "456 Oak Ave", City = "Shelbyville" }
        };
        var result = _validator.Validate(order);
        // ShippingAddress is null → NotNull fires, but no nested failures for ShippingAddress
        ValidationAssert.HasError(result, "ShippingAddress");
        // No "ShippingAddress.Street" type failures since ShippingAddress is null
        Assert.DoesNotContain(result.Failures.ToArray(), f => f.PropertyName.StartsWith("ShippingAddress.", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Multiple_Nested_Failures_AllReported()
    {
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
            BillingAddress = new Address { Street = "", City = "" }
        };
        var result = _validator.Validate(order);
        ValidationAssert.HasError(result, "BillingAddress.Street");
        ValidationAssert.HasError(result, "BillingAddress.City");
        Assert.Equal(2, result.Failures.Length);
    }

    [Fact]
    public void Direct_And_Nested_Failures_Reported_Together()
    {
        var order = new Order
        {
            Reference = "",  // direct failure
            ShippingAddress = new Address { Street = "123 Main St", City = "Springfield" },
            BillingAddress = new Address { Street = "", City = "Shelbyville" }  // nested failure
        };
        var result = _validator.Validate(order);
        ValidationAssert.HasError(result, "Reference");
        ValidationAssert.HasError(result, "BillingAddress.Street");
        Assert.Equal(2, result.Failures.Length);
    }

    [Fact]
    public void Nested_ShippingAddress_NonNull_Invalid_ReportsDotPrefixedFailure()
    {
        // ShippingAddress has [NotNull] AND its type [Validate] — when non-null the nested validator runs
        var order = new Order
        {
            Reference = "ORD-001",
            ShippingAddress = new Address { Street = "", City = "Springfield" },
            BillingAddress = new Address { Street = "456 Oak Ave", City = "Shelbyville" }
        };
        var result = _validator.Validate(order);
        ValidationAssert.HasError(result, "ShippingAddress.Street");
        Assert.DoesNotContain(result.Failures.ToArray(), f => string.Equals(f.PropertyName, "ShippingAddress", System.StringComparison.Ordinal));
    }
}
