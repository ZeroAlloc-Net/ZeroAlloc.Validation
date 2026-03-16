using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class PlaceholderTests
{
    private readonly PlaceholderModelValidator _validator = new();

    [Fact]
    public void Placeholder_PropertyName_AppearsInMessage()
    {
        var result = _validator.Validate(new PlaceholderModel { Name = "", Age = 1, Bio = "ok", Score = 50 });
        ValidationAssert.HasErrorWithMessage(result, "Name", "'Name' is required");
    }

    [Fact]
    public void Placeholder_ComparisonValue_AppearsInMessage()
    {
        var result = _validator.Validate(new PlaceholderModel { Name = "x", Age = 0, Bio = "ok", Score = 50 });
        ValidationAssert.HasErrorWithMessage(result, "Age", "'Age' must be greater than 0");
    }

    [Fact]
    public void Placeholder_MinMaxLength_AppearsInMessage()
    {
        var result = _validator.Validate(new PlaceholderModel { Name = "x", Age = 1, Bio = "x", Score = 50 });
        ValidationAssert.HasErrorWithMessage(result, "Bio", "'Bio' must be 2\u201350 chars");
    }

    [Fact]
    public void Placeholder_FromTo_AppearsInMessage()
    {
        var result = _validator.Validate(new PlaceholderModel { Name = "x", Age = 1, Bio = "ok", Score = 0 });
        ValidationAssert.HasErrorWithMessage(result, "Score", "'Score' must be between 0 and 100");
    }

    [Fact]
    public void Placeholder_ValidModel_NoErrors()
    {
        ValidationAssert.NoErrors(_validator.Validate(new PlaceholderModel
        {
            Name = "Alice",
            Age = 25,
            Bio = "Developer",
            Score = 50
        }));
    }
}
