using System.Linq;
using Xunit;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class DeepNestingTests
{
    private readonly DepotValidator _validator = new(new DeliveryZoneValidator(new PostalCodeValidator()));

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
        Assert.Equal("Code is required.", failures.Single(f => string.Equals(f.PropertyName, "Zone.PostalCode.Code", System.StringComparison.Ordinal)).ErrorMessage);
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

    [Fact]
    public void ThreeLevel_Failure_PreservesErrorCode()
    {
        var depot = new Depot
        {
            Id = "D-01",
            Zone = new DeliveryZone { Name = "North", PostalCode = new PostalCode { Code = "" } }
        };
        var result = _validator.Validate(depot);
        var failure = result.Failures.ToArray()
            .First(f => string.Equals(f.PropertyName, "Zone.PostalCode.Code", StringComparison.Ordinal));
        Assert.Equal("CODE_REQUIRED", failure.ErrorCode);
    }
}
