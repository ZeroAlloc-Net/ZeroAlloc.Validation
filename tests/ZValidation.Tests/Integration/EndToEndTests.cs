using System.Collections.Generic;
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
        var failures = result.Failures.ToArray();
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "BillingAddress.Street");
        Assert.Equal("Street is required.", failures.First(f => f.PropertyName == "BillingAddress.Street").ErrorMessage);
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
        Assert.DoesNotContain(result.Failures.ToArray(), f => f.PropertyName == "ShippingAddress");
    }
}

// Three-level deep nesting: Depot → DeliveryZone → PostalCode
[Validate]
public class PostalCode
{
    [NotEmpty(Message = "Code is required.")]
    public string Code { get; set; } = "";
}

[Validate]
public class DeliveryZone
{
    [NotEmpty(Message = "Zone name is required.")]
    public string Name { get; set; } = "";

    public PostalCode PostalCode { get; set; } = new();
}

[Validate]
public class Depot
{
    [NotEmpty]
    public string Id { get; set; } = "";

    public DeliveryZone Zone { get; set; } = new();
}

public class DeepNestingTests
{
    private readonly DepotValidator _validator = new();

    [Fact]
    public void Valid_Depot_PassesValidation()
    {
        var depot = new Depot
        {
            Id = "D-01",
            Zone = new DeliveryZone { Name = "North", PostalCode = new PostalCode { Code = "12345" } }
        };
        ValidationAssert.NoErrors(_validator.Validate(depot));
    }

    [Fact]
    public void ThreeLevel_Deep_Failure_ReportsFullDotPath()
    {
        var depot = new Depot
        {
            Id = "D-01",
            Zone = new DeliveryZone { Name = "North", PostalCode = new PostalCode { Code = "" } }
        };
        var result = _validator.Validate(depot);
        var failures = result.Failures.ToArray();
        ValidationAssert.HasError(result, "Zone.PostalCode.Code");
        Assert.Equal("Code is required.", failures.Single(f => f.PropertyName == "Zone.PostalCode.Code").ErrorMessage);
    }

    [Fact]
    public void ThreeLevel_Failures_At_Multiple_Levels_AllReported()
    {
        var depot = new Depot
        {
            Id = "D-01",
            Zone = new DeliveryZone { Name = "", PostalCode = new PostalCode { Code = "" } }
        };
        var result = _validator.Validate(depot);
        ValidationAssert.HasError(result, "Zone.Name");
        ValidationAssert.HasError(result, "Zone.PostalCode.Code");
        Assert.Equal(2, result.Failures.Length);
    }
}

[Validate]
public class LineItem
{
    [NotEmpty(Message = "SKU is required.")]
    public string Sku { get; set; } = "";

    [GreaterThan(0, Message = "Quantity must be positive.")]
    public int Quantity { get; set; }
}

[Validate]
public class Cart
{
    [NotEmpty]
    public string CustomerId { get; set; } = "";

    public List<LineItem> Items { get; set; } = [];
}

public class CollectionValidationTests
{
    private readonly CartValidator _validator = new();

    [Fact]
    public void Valid_Cart_PassesValidation()
    {
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items =
            [
                new LineItem { Sku = "ABC", Quantity = 2 },
                new LineItem { Sku = "DEF", Quantity = 1 }
            ]
        };
        ValidationAssert.NoErrors(_validator.Validate(cart));
    }

    [Fact]
    public void Item_InvalidSku_ReportsBracketIndexedFailure()
    {
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items = [ new LineItem { Sku = "", Quantity = 1 } ]
        };
        var result = _validator.Validate(cart);
        var failures = result.Failures.ToArray();
        ValidationAssert.HasError(result, "Items[0].Sku");
        Assert.Equal("SKU is required.", failures.Single(f => f.PropertyName == "Items[0].Sku").ErrorMessage);
    }

    [Fact]
    public void SecondItem_Invalid_ReportsCorrectIndex()
    {
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items =
            [
                new LineItem { Sku = "ABC", Quantity = 1 },
                new LineItem { Sku = "", Quantity = 1 }
            ]
        };
        var result = _validator.Validate(cart);
        ValidationAssert.HasError(result, "Items[1].Sku");
        Assert.DoesNotContain(result.Failures.ToArray(), f => f.PropertyName == "Items[0].Sku");
    }

    [Fact]
    public void Multiple_Items_Multiple_Failures_AllReported()
    {
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items =
            [
                new LineItem { Sku = "", Quantity = 0 },
                new LineItem { Sku = "ABC", Quantity = 1 },
                new LineItem { Sku = "", Quantity = -1 }
            ]
        };
        var result = _validator.Validate(cart);
        ValidationAssert.HasError(result, "Items[0].Sku");
        ValidationAssert.HasError(result, "Items[0].Quantity");
        ValidationAssert.HasError(result, "Items[2].Sku");
        ValidationAssert.HasError(result, "Items[2].Quantity");
        Assert.Equal(4, result.Failures.Length);
    }

    [Fact]
    public void Null_Collection_IsSkipped()
    {
        var cart = new Cart { CustomerId = "C-001", Items = null! };
        ValidationAssert.NoErrors(_validator.Validate(cart));
    }

    [Fact]
    public void Direct_And_Collection_Failures_ReportedTogether()
    {
        var cart = new Cart
        {
            CustomerId = "",  // direct failure
            Items = [ new LineItem { Sku = "", Quantity = 1 } ]  // collection failure
        };
        var result = _validator.Validate(cart);
        ValidationAssert.HasError(result, "CustomerId");
        ValidationAssert.HasError(result, "Items[0].Sku");
        Assert.Equal(2, result.Failures.Length);
    }
}
