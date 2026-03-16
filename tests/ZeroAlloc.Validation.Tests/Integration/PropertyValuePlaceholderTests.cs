using System.Linq;
using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class PropertyValuePlaceholderTests
{
    private readonly PropertyValueModelValidator _validator = new();

    [Fact]
    public void ValueType_FailureMessage_ContainsActualValue()
    {
        var result = _validator.Validate(new PropertyValueModel { Age = -5, Name = "ok", Code = "ok" });
        var failure = result.Failures.ToArray()
            .First(f => string.Equals(f.PropertyName, "Age", System.StringComparison.Ordinal));
        Assert.Equal("Age must be > 0, got -5.", failure.ErrorMessage);
    }

    [Fact]
    public void StringType_FailureMessage_ContainsActualValue()
    {
        var result = _validator.Validate(new PropertyValueModel { Age = 1, Name = "toolong", Code = "ok" });
        var failure = result.Failures.ToArray()
            .First(f => string.Equals(f.PropertyName, "Name", System.StringComparison.Ordinal));
        Assert.Equal("Name 'toolong' exceeds 5 characters.", failure.ErrorMessage);
    }

    [Fact]
    public void NullableValueType_FailureMessage_ContainsActualValue()
    {
        var result = _validator.Validate(new PropertyValueModel { Age = 1, Name = "ok", Score = -1.5, Code = "ok" });
        var failure = result.Failures.ToArray()
            .First(f => string.Equals(f.PropertyName, "Score", System.StringComparison.Ordinal));
        Assert.Equal("Score must be > 0, got -1.5.", failure.ErrorMessage);
    }

    [Fact]
    public void MixedPlaceholders_CompileTimeAndRuntime_BothResolved()
    {
        var result = _validator.Validate(new PropertyValueModel { Age = 1, Name = "ok", Points = 50, Code = "ok" });
        var failure = result.Failures.ToArray()
            .First(f => string.Equals(f.PropertyName, "Points", System.StringComparison.Ordinal));
        Assert.Equal("Points must be > 100, got 50.", failure.ErrorMessage);
    }

    [Fact]
    public void NoPropertyValueInMessage_PlainStringUnaffected()
    {
        var result = _validator.Validate(new PropertyValueModel { Age = 1, Name = "ok", Code = "x" });
        var failure = result.Failures.ToArray()
            .First(f => string.Equals(f.PropertyName, "Code", System.StringComparison.Ordinal));
        Assert.Equal("Too short.", failure.ErrorMessage);
    }

    [Fact]
    public void ValidModel_NoErrors()
    {
        var model = new PropertyValueModel { Age = 1, Name = "hi", Score = 5.0, Points = 200, Code = "ab" };
        ValidationAssert.NoErrors(_validator.Validate(model));
    }
}
