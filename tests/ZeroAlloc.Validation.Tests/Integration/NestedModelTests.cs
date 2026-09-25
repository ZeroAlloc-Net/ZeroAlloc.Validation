using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

// Issue #207: a [Validate] model nested in another type used to fail with CS0246 in the
// generated validator. These tests compile only if every nested model gets a validator under
// its containing-type-qualified name, and they run each one.
public class NestedModelTests
{
    [Fact]
    public void OneLevel_NestedModel_Validates()
    {
        var validator = new NestedModelHost_RequestValidator();

        ValidationAssert.NoErrors(validator.Validate(new NestedModelHost.Request { Name = "Ada" }));
        ValidationAssert.HasError(validator.Validate(new NestedModelHost.Request { Name = "" }), "Name");
    }

    [Fact]
    public void TwoLevel_NestedModel_WithSameSimpleName_Validates()
    {
        var validator = new NestedModelLevel1_Level2_RequestValidator();

        ValidationAssert.NoErrors(validator.Validate(new NestedModelLevel1.Level2.Request { Quantity = 1 }));
        ValidationAssert.HasError(validator.Validate(new NestedModelLevel1.Level2.Request { Quantity = 0 }), "Quantity");
    }

    [Fact]
    public void SameNamedModels_InTwoContainers_GetDistinctValidators()
    {
        ValidatorFor<NestedModelHost.Request> first = new NestedModelHost_RequestValidator();
        ValidatorFor<NestedModelLevel1.Level2.Request> second = new NestedModelLevel1_Level2_RequestValidator();

        Assert.NotEqual(first.GetType(), second.GetType());
    }

    [Fact]
    public void NestedModel_AsNestedMember_OfNestedModel_Validates()
    {
        var validator = new NestedModelHost_EnvelopeValidator(new NestedModelLevel1_Level2_RequestValidator());

        ValidationAssert.NoErrors(validator.Validate(new NestedModelHost.Envelope
        {
            Line = new NestedModelLevel1.Level2.Request { Quantity = 2 },
        }));
        ValidationAssert.HasError(validator.Validate(new NestedModelHost.Envelope
        {
            Line = new NestedModelLevel1.Level2.Request { Quantity = 0 },
        }), "Line.Quantity");
    }

    [Fact]
    public void TopLevelModel_ComposesNestedModelValidators()
    {
        var validator = new NestedModelOrderValidator(
            new NestedModelHost_RequestValidator(),
            new NestedModelLevel1_Level2_RequestValidator(),
            new NestedModelHost.ReferenceValidator());

        var valid = new NestedModelOrder
        {
            Customer = new NestedModelHost.Request { Name = "Ada" },
            Lines = [new NestedModelLevel1.Level2.Request { Quantity = 3 }],
            Reference = new NestedModelHost.Reference { Code = "R-1" },
        };
        ValidationAssert.NoErrors(validator.Validate(valid));

        var invalid = new NestedModelOrder
        {
            Customer = new NestedModelHost.Request { Name = "" },
            Lines = [new NestedModelLevel1.Level2.Request { Quantity = 0 }],
            Reference = new NestedModelHost.Reference { Code = "rejected" },
        };
        var result = validator.Validate(invalid);
        ValidationAssert.HasError(result, "Customer.Name");
        ValidationAssert.HasError(result, "Lines[0].Quantity");
        ValidationAssert.HasError(result, "Reference.Code");
    }
}
