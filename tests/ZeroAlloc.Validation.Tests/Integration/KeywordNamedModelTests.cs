using System.Collections.Generic;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

// Issue #242: a property or method named with a keyword broke the generated validator. This
// compiles only if every generated access and call escapes the name, and the tests check that
// failures still report the name as declared, without the @.
public class KeywordNamedModelTests
{
    private readonly KeywordNamedModelValidator _validator = new(new KeywordNamedChildValidator(), new KeywordNamedChildValidator());

    private static KeywordNamedModel Valid() => new()
    {
        @class = "a",
        @event = 1,
        @var = "ok",
        @default = new KeywordNamedChild { @namespace = "n" },
        @this = new List<KeywordNamedChild> { new() { @namespace = "n" } },
    };

    [Fact]
    public void ValidModel_Passes() => ValidationAssert.NoErrors(_validator.Validate(Valid()));

    [Fact]
    public void KeywordProperty_ReportsItsNameAndValue()
    {
        var model = Valid();
        model.@class = "";

        var result = _validator.Validate(model);

        ValidationAssert.HasErrorWithMessage(result, "class", "class was ''.");
    }

    [Fact]
    public void KeywordConditionMethod_IsCalled()
    {
        var model = Valid();
        model.@event = 0;

        ValidationAssert.HasError(_validator.Validate(model), "event");

        model.CheckEvent = false;
        ValidationAssert.NoErrors(_validator.Validate(model));
    }

    [Fact]
    public void KeywordPredicateMethod_IsCalled()
    {
        var model = Valid();
        model.@var = "bad";

        ValidationAssert.HasError(_validator.Validate(model), "var");
    }

    [Fact]
    public void KeywordNestedAndCollectionProperties_AreValidated()
    {
        var model = Valid();
        model.@default = new KeywordNamedChild { @namespace = "" };
        model.@this = new List<KeywordNamedChild> { new() { @namespace = "" } };

        var result = _validator.Validate(model);

        ValidationAssert.HasError(result, "default.namespace");
        ValidationAssert.HasError(result, "this[0].namespace");
    }

    [Fact]
    public void KeywordCustomValidationMethod_IsCalled()
    {
        var model = Valid();
        model.@class = "custom";

        ValidationAssert.HasError(_validator.Validate(model), "return");
    }
}
