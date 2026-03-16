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

    [Fact]
    public void StopOnFirstFailure_WhenConditionFalse_SubsequentRuleStillRuns()
    {
        // When=false → first rule condition is false (not fired) → else if runs → MinLength fires
        var result = _validator.Validate(new CascadeModel
        {
            StopName = "ok", ContinueName = "hello",
            ConditionalCheck = false,
            ConditionalStop = "x" // length 1 — fails MinLength(3)
        });
        ValidationAssert.HasError(result, "ConditionalStop");
    }

    [Fact]
    public void StopOnFirstFailure_WhenConditionTrue_FirstRuleFires_SecondSkipped()
    {
        // When=true → first rule fires (NotEmpty on "") → else if skipped → only 1 failure
        var result = _validator.Validate(new CascadeModel
        {
            StopName = "ok", ContinueName = "hello",
            ConditionalCheck = true,
            ConditionalStop = "" // empty — fails NotEmpty (When=true)
        });
        var conditionalFailures = result.Failures.ToArray()
            .Where(f => string.Equals(f.PropertyName, "ConditionalStop", StringComparison.Ordinal))
            .ToArray();
#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        Assert.Single(conditionalFailures); // stop mode: only NotEmpty fires
#pragma warning restore HLQ005
    }
}
