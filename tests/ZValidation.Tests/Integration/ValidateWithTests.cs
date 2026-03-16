using System;
using System.Linq;
using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class ValidateWithTests
{
    private readonly LocationValidator _validator = new(new CoordinateValidator());

    [Fact]
    public void Valid_Location_PassesValidation()
    {
        var location = new Location { Name = "Home", Point = new Coordinate { Lat = 51.5, Lng = -0.1 } };
        ValidationAssert.NoErrors(_validator.Validate(location));
    }

    [Fact]
    public void Invalid_Coordinate_ReportsDotPrefixedFailure()
    {
        var location = new Location { Name = "Home", Point = new Coordinate { Lat = 200, Lng = 0 } };
        var result = _validator.Validate(location);
        ValidationAssert.HasError(result, "Point.Lat");
    }

    [Fact]
    public void Invalid_Coordinate_PreservesErrorCode_FromHandWrittenValidator()
    {
        var location = new Location { Name = "Home", Point = new Coordinate { Lat = 200, Lng = 0 } };
        var result = _validator.Validate(location);
        var failure = result.Failures.ToArray()
            .First(f => string.Equals(f.PropertyName, "Point.Lat", StringComparison.Ordinal));
        Assert.Equal("LAT_RANGE", failure.ErrorCode);
    }

    [Fact]
    public void Both_Direct_And_ValidateWith_Failures_Reported()
    {
        var location = new Location { Name = "", Point = new Coordinate { Lat = 200, Lng = 400 } };
        var result = _validator.Validate(location);
        ValidationAssert.HasError(result, "Name");
        ValidationAssert.HasError(result, "Point.Lat");
        ValidationAssert.HasError(result, "Point.Lng");
        Assert.Equal(3, result.Failures.Length);
    }
}
