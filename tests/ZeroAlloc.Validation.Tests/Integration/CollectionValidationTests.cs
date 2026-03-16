using System.Linq;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class CollectionValidationTests
{
    private readonly CartValidator _validator = new(new LineItemValidator());

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
        Assert.Equal("SKU is required.", failures.Single(f => string.Equals(f.PropertyName, "Items[0].Sku", System.StringComparison.Ordinal)).ErrorMessage);
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
        Assert.DoesNotContain(result.Failures.ToArray(), f => string.Equals(f.PropertyName, "Items[0].Sku", System.StringComparison.Ordinal));
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

    [Fact]
    public void Collection_Failure_PreservesErrorCode()
    {
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items = [ new LineItem { Sku = "", Quantity = 1 } ]
        };
        var result = _validator.Validate(cart);
        var failure = result.Failures.ToArray()
            .First(f => string.Equals(f.PropertyName, "Items[0].Sku", StringComparison.Ordinal));
        Assert.Equal("SKU_REQUIRED", failure.ErrorCode);
    }

    [Fact]
    public void Null_Items_In_Collection_AreSkipped()
    {
        // Generator emits: if (varItem is not null) { validate } — null items are skipped
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items = new System.Collections.Generic.List<LineItem> { null!, new LineItem { Sku = "ABC", Quantity = 1 }, null! }
        };
        ValidationAssert.NoErrors(_validator.Validate(cart));
    }

    [Fact]
    public void Null_Item_IndexContinues_AfterNullItem()
    {
        // Null items still increment the index counter
        var cart = new Cart
        {
            CustomerId = "C-001",
            Items = new System.Collections.Generic.List<LineItem>
            {
                null!,                                         // index 0 — skipped
                null!,                                         // index 1 — skipped
                new LineItem { Sku = "", Quantity = 1 }        // index 2 — fails
            }
        };
        var result = _validator.Validate(cart);
        ValidationAssert.HasError(result, "Items[2].Sku");
    }
}
