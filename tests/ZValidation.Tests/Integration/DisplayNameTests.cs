using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class DisplayNameTests
{
    private readonly DisplayNameModelValidator _validator = new();

    [Fact]
    public void DisplayName_AppearsInDefaultMessage()
    {
        var model = new DisplayNameModel { Forename = "" };
        var result = _validator.Validate(model);
        ValidationAssert.HasErrorWithMessage(result, "Forename", "First Name must not be empty.");
    }

    [Fact]
    public void DisplayName_AppearsForAllRulesOnProperty()
    {
        var model = new DisplayNameModel { Forename = "x" };  // passes NotEmpty, fails MinLength(2)
        var result = _validator.Validate(model);
        ValidationAssert.HasErrorWithMessage(result, "Forename", "First Name must be at least 2 characters.");
    }

    [Fact]
    public void DisplayName_SubstitutesPropertyNamePlaceholder()
    {
        var model = new DisplayNameModel { ZipCode = "abc" };
        var result = _validator.Validate(model);
        ValidationAssert.HasErrorWithMessage(result, "ZipCode", "ZIP Code must be 5 digits.");
    }

    [Fact]
    public void DisplayName_PropertyNameInFailureIsRawCSharpName()
    {
        var model = new DisplayNameModel { Forename = "" };
        var result = _validator.Validate(model);
        // ValidationFailure.PropertyName must stay "Forename", not "First Name"
        ValidationAssert.HasError(result, "Forename");
    }

    [Fact]
    public void NoDisplayName_UsesRawPropertyName()
    {
        var model = new DisplayNameModel { NoDisplayName = "" };
        var result = _validator.Validate(model);
        ValidationAssert.HasErrorWithMessage(result, "NoDisplayName", "NoDisplayName must not be empty.");
    }
}
