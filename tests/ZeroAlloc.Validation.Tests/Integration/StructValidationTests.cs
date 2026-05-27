using Xunit;
using ZeroAlloc.Validation;

#pragma warning disable MA0048 // multiple types intentionally co-located with the test class

namespace ZeroAlloc.Validation.Tests.Integration;

public class StructValidationTests
{
    [Fact]
    public void ReadonlyRecordStruct_Validate_HappyPath_ReportsValid()
    {
        var validator = new RrsCommandValidator();
        var result = validator.Validate(new RrsCommand(Total: 10));
        Assert.True(result.IsValid);
        Assert.True(result.Failures.IsEmpty);
    }

    [Fact]
    public void ReadonlyRecordStruct_Validate_SadPath_ReportsFailureOnTotal()
    {
        var validator = new RrsCommandValidator();
        var result = validator.Validate(new RrsCommand(Total: 0));
        Assert.False(result.IsValid);
        Assert.Equal(1, result.Failures.Length);
        Assert.Equal(nameof(RrsCommand.Total), result.Failures[0].PropertyName);
    }
}

[Validate]
public readonly record struct RrsCommand([property: GreaterThan(0)] int Total);
