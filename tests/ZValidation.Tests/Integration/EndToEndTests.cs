using Xunit;
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

[Validate]
public class Person
{
    [NotEmpty(Message = "Name is required.")]
    [ZValidation.MaxLength(100)]
    public string Name { get; set; } = "";

    [ZValidation.EmailAddress]
    public string Email { get; set; } = "";

    [GreaterThan(0)]
    [LessThan(120)]
    public int Age { get; set; }
}

public class EndToEndTests
{
    private readonly PersonValidator _validator = new();

    [Fact]
    public void Valid_Person_PassesValidation()
    {
        var person = new Person { Name = "Alice", Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Empty_Name_FailsWithCustomMessage()
    {
        var person = new Person { Name = "", Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "Name");
        Assert.Equal("Name is required.", result.Failures[0].ErrorMessage);
    }

    [Fact]
    public void Name_TooLong_FailsValidation()
    {
        var person = new Person { Name = new string('x', 101), Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        ValidationAssert.HasError(result, "Name");
    }

    [Fact]
    public void Empty_Name_Only_ReportsOneFailureForNameProperty()
    {
        var person = new Person { Name = "", Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        Assert.Equal(1, result.Failures.ToArray().Count(f => f.PropertyName == "Name"));
    }

    [Fact]
    public void Invalid_Email_FailsValidation()
    {
        var person = new Person { Name = "Alice", Email = "not-an-email", Age = 30 };
        var result = _validator.Validate(person);
        ValidationAssert.HasError(result, "Email");
    }

    [Fact]
    public void Age_Zero_FailsGreaterThan()
    {
        var person = new Person { Name = "Alice", Email = "alice@example.com", Age = 0 };
        var result = _validator.Validate(person);
        ValidationAssert.HasError(result, "Age");
    }

    [Fact]
    public void Multiple_Invalid_Properties_ReportsAllFailures()
    {
        var person = new Person { Name = "", Email = "bad", Age = -1 };
        var result = _validator.Validate(person);
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "Name");
        ValidationAssert.HasError(result, "Email");
        ValidationAssert.HasError(result, "Age");
    }
}

[Validate]
public class Address
{
    [NotEmpty(Message = "Street is required.")]
    public string Street { get; set; } = "";

    [NotEmpty(Message = "City is required.")]
    public string City { get; set; } = "";
}

[Validate]
public class Order
{
    [NotEmpty]
    public string Reference { get; set; } = "";

    [NotNull]
    public Address? ShippingAddress { get; set; }

    // Address has [Validate] → automatically nested
    public Address BillingAddress { get; set; } = new();
}

public class NestedValidationTests
{
    private readonly OrderValidator _validator = new();

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
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "BillingAddress.Street");
        Assert.Equal("Street is required.", result.Failures.ToArray()
            .First(f => f.PropertyName == "BillingAddress.Street").ErrorMessage);
    }

    [Fact]
    public void Nested_BillingAddress_Null_IsSkipped()
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
        Assert.DoesNotContain(result.Failures.ToArray(), f => f.PropertyName.StartsWith("ShippingAddress."));
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
    }
}
