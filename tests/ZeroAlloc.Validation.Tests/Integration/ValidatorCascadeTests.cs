using System;
using System.Linq;
using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class ValidatorCascadeTests
{
    private readonly ValidatorCascadeModelValidator _flatValidator = new();
    private readonly ValidatorCascadeWithNestedModelValidator _nestedValidator = new(new AddressValidator());

    // --- Flat path ---

    [Fact]
    public void FlatPath_FirstPropertyFails_SecondNotValidated()
    {
        var model = new ValidatorCascadeModel { Reference = "", Quantity = -1 };
        var result = _flatValidator.Validate(model);
        ValidationAssert.HasError(result, "Reference");
        Assert.DoesNotContain(result.Failures.ToArray(), f =>
            string.Equals(f.PropertyName, "Quantity", StringComparison.Ordinal));
    }

    [Fact]
    public void FlatPath_FirstPropertyPasses_SecondValidated()
    {
        var model = new ValidatorCascadeModel { Reference = "ok", Quantity = -1 };
        var result = _flatValidator.Validate(model);
        ValidationAssert.HasError(result, "Quantity");
    }

    [Fact]
    public void FlatPath_AllPass_NoFailures()
    {
        var model = new ValidatorCascadeModel { Reference = "ok", Quantity = 5 };
        var result = _flatValidator.Validate(model);
        ValidationAssert.NoErrors(result);
    }

    // --- Nested path ---

    [Fact]
    public void NestedPath_FirstPropertyFails_NestedNotValidated()
    {
        var model = new ValidatorCascadeWithNestedModel
        {
            Reference = "",
            ShippingAddress = new Address { Street = "", City = "" }
        };
        var result = _nestedValidator.Validate(model);
        ValidationAssert.HasError(result, "Reference");
        Assert.DoesNotContain(result.Failures.ToArray(), f =>
            f.PropertyName.StartsWith("ShippingAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedPath_FirstPropertyPasses_NestedValidated()
    {
        var model = new ValidatorCascadeWithNestedModel
        {
            Reference = "ok",
            ShippingAddress = new Address { Street = "", City = "" }
        };
        var result = _nestedValidator.Validate(model);
        Assert.Contains(result.Failures.ToArray(), f =>
            f.PropertyName.StartsWith("ShippingAddress", StringComparison.Ordinal));
    }
}
