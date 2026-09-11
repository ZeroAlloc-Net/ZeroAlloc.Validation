using System;
using System.Linq;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Model-level fail-fast returns a single-failure result directly instead of filling a scratch
/// buffer and copying out of it. These pin the semantics that optimization must not disturb.
/// </summary>
public class FailFastDirectReturnTests
{
    [Fact]
    public void SingleRuleGroup_ReportsTheFailure()
    {
        var result = new FailFastSingleModelValidator()
            .Validate(new FailFastSingleModel { PlayerId = "", Score = 1 });

        var failure = SingleFailure(result);
        Assert.Equal("PlayerId", failure.PropertyName);
        Assert.Equal("PlayerId must not be empty.", failure.ErrorMessage);
    }

    [Fact]
    public void SingleRuleGroup_PreservesErrorCodeAndSeverity()
    {
        var result = new FailFastSingleModelValidator()
            .Validate(new FailFastSingleModel { PlayerId = "", Score = 1 });

        var failure = SingleFailure(result);
        Assert.Equal("PLAYER_REQUIRED", failure.ErrorCode);
        Assert.Equal(Severity.Warning, failure.Severity);
    }

    [Fact]
    public void FailFast_StopsAtFirstFailingProperty()
    {
        // Both PlayerId and Score are invalid; only the first is reported.
        var result = new FailFastSingleModelValidator()
            .Validate(new FailFastSingleModel { PlayerId = "", Score = 0 });

        var failure = SingleFailure(result);
        Assert.Equal("PlayerId", failure.PropertyName);
    }

    [Fact]
    public void FailFast_LaterPropertyStillReportedWhenEarlierPasses()
    {
        var result = new FailFastSingleModelValidator()
            .Validate(new FailFastSingleModel { PlayerId = "p1", Score = 0 });

        Assert.Equal("Score", SingleFailure(result).PropertyName);
    }

    [Fact]
    public void WhenGuard_SuppressesRuleOnDirectReturnPath()
    {
        var result = new FailFastSingleModelValidator()
            .Validate(new FailFastSingleModel { PlayerId = "p1", Score = 1, Region = null, RequiresRegion = false });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void WhenGuard_FiresRuleOnDirectReturnPath()
    {
        var result = new FailFastSingleModelValidator()
            .Validate(new FailFastSingleModel { PlayerId = "p1", Score = 1, Region = null, RequiresRegion = true });

        Assert.Equal("Region", SingleFailure(result).PropertyName);
    }

    [Fact]
    public void ValidModel_IsValidAndAllocatesNoFailures()
    {
        var result = new FailFastSingleModelValidator()
            .Validate(new FailFastSingleModel { PlayerId = "p1", Score = 1 });

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures.ToArray());
    }

    [Fact]
    public void PerPropertyStop_ReportsOnlyFirstMatchingRule()
    {
        // "" violates NotEmpty and MinLength; per-property stop means only NotEmpty is reported.
        var result = new FailFastCascadeModelValidator()
            .Validate(new FailFastCascadeModel { Region = "", Score = 1 });

        var failure = SingleFailure(result);
        Assert.Equal("Region", failure.PropertyName);
        Assert.Equal("Region must not be empty.", failure.ErrorMessage);
    }

    [Fact]
    public void PerPropertyStop_ReportsLaterRuleWhenEarlierPasses()
    {
        // "ab" passes NotEmpty but violates MinLength(3).
        var result = new FailFastCascadeModelValidator()
            .Validate(new FailFastCascadeModel { Region = "ab", Score = 1 });

        Assert.Equal("Region", SingleFailure(result).PropertyName);
    }

    [Fact]
    public void PerPropertyStop_ValidModelIsValid()
    {
        var result = new FailFastCascadeModelValidator()
            .Validate(new FailFastCascadeModel { Region = "eu-west", Score = 1 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void MixedModel_DirectReturnGroupStillStopsFirst()
    {
        var result = new FailFastMixedModelValidator()
            .Validate(new FailFastMixedModel { PlayerId = "", Region = "" });

        Assert.Equal("PlayerId", SingleFailure(result).PropertyName);
    }

    [Fact]
    public void MixedModel_BufferedGroupStillReportsEveryFailure()
    {
        // Region has two rules and no per-property stop, so both must be reported —
        // this group keeps the buffer and must not be collapsed to a direct return.
        var result = new FailFastMixedModelValidator()
            .Validate(new FailFastMixedModel { PlayerId = "p1", Region = "" });

        var failures = result.Failures.ToArray();
        Assert.Equal(2, failures.Length);
        Assert.All(failures, f => Assert.Equal("Region", f.PropertyName));
    }

    [Fact]
    public void MixedModel_ValidModelIsValid()
    {
        var result = new FailFastMixedModelValidator()
            .Validate(new FailFastMixedModel { PlayerId = "p1", Region = "eu-west" });

        Assert.True(result.IsValid);
    }

    /// <summary>The one failure in <paramref name="result"/>, asserting there is exactly one.</summary>
    private static ValidationFailure SingleFailure(ValidationResult result)
    {
#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        return Assert.Single(result.Failures.ToArray());
#pragma warning restore HLQ005
    }
}
