using System;
using System.Linq;
using Xunit;
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

public class CascadeTests
{
    private readonly CascadeModelValidator _validator = new();

    [Fact]
    public void StopOnFirstFailure_EmptyValue_ReportsOnlyOneFailure()
    {
        // Empty string violates NotEmpty; MinLength and MaxLength should NOT also fire (stop mode)
        var result = _validator.Validate(new CascadeModel { StopName = "", ContinueName = "valid" });
        var stopFailures = result.Failures.ToArray()
            .Where(f => string.Equals(f.PropertyName, "StopName", StringComparison.Ordinal))
            .ToArray();
#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        Assert.Single(stopFailures);
#pragma warning restore HLQ005
    }

    [Fact]
    public void ContinueMode_EmptyValue_ReportsAllApplicableFailures()
    {
        // Empty string violates both NotEmpty (IsNullOrEmpty) and MinLength (Length < 5)
        // Both run independently in continue mode — multiple failures expected
        var result = _validator.Validate(new CascadeModel { StopName = "valid", ContinueName = "" });
        var continueFailures = result.Failures.ToArray()
            .Where(f => string.Equals(f.PropertyName, "ContinueName", StringComparison.Ordinal))
            .ToArray();
        Assert.True(continueFailures.Length > 1, "Continue mode should report multiple failures");
    }

    [Fact]
    public void StopOnFirstFailure_ValidValue_NoFailures()
    {
        var result = _validator.Validate(new CascadeModel { StopName = "valid", ContinueName = "valid" });
        Assert.DoesNotContain(result.Failures.ToArray(), f =>
            string.Equals(f.PropertyName, "StopName", StringComparison.Ordinal));
    }
}
