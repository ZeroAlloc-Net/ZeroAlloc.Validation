using System;
using System.Linq;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Length rules dereference the value, so on a nullable property they are guarded. A null reports
/// nothing from a length rule — that is <c>[NotEmpty]</c>'s or <c>[NotNull]</c>'s job — rather than
/// throwing, which is what happened before the guard.
/// </summary>
public class NullableLengthRuleTests
{
    [Fact]
    public void NullValue_DoesNotThrow()
    {
        var validator = new NullableLengthModelValidator();

        // The whole point: this used to be a NullReferenceException.
        var result = validator.Validate(new NullableLengthModel());

        Assert.NotNull(result.Failures.ToArray());
    }

    [Fact]
    public void NullValue_ReportsOnlyTheEmptinessFailure()
    {
        var result = new NullableLengthModelValidator()
            .Validate(new NullableLengthModel { Tenant = null, Region = null, Code = null, Items = null });

        // Tenant carries [NotEmpty] so null is reported once. Region, Code and Items carry only
        // length rules, which say nothing about a missing value.
        var names = result.Failures.ToArray().Select(f => f.PropertyName).ToArray();
        Assert.Equal(["Tenant"], names);
    }

    [Fact]
    public void NullValue_WithNotNull_ReportsOnceFromNotNull()
    {
        var result = new NullableLengthNotNullModelValidator()
            .Validate(new NullableLengthNotNullModel { Tenant = null });

#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        var failure = Assert.Single(result.Failures.ToArray());
#pragma warning restore HLQ005
        Assert.Equal("Tenant", failure.PropertyName);
        Assert.Equal("Tenant must not be null.", failure.ErrorMessage);
    }

    [Fact]
    public void NonNullValue_StillCheckedAgainstMinLength()
    {
        var result = new NullableLengthModelValidator()
            .Validate(new NullableLengthModel { Tenant = "ab" });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Tenant", StringComparison.Ordinal)
                 && f.ErrorMessage.Contains("at least 3", StringComparison.Ordinal));
    }

    [Fact]
    public void NonNullValue_StillCheckedAgainstMaxLength()
    {
        var result = new NullableLengthModelValidator()
            .Validate(new NullableLengthModel { Tenant = "abc", Region = "toolong" });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Region", StringComparison.Ordinal));
    }

    [Fact]
    public void NonNullValue_StillCheckedAgainstLengthRange()
    {
        var result = new NullableLengthModelValidator()
            .Validate(new NullableLengthModel { Tenant = "abc", Code = "a" });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Code", StringComparison.Ordinal));
    }

    [Fact]
    public void NullArray_DoesNotThrowAndReportsNothing()
    {
        var result = new NullableLengthModelValidator()
            .Validate(new NullableLengthModel { Tenant = "abc", Items = null });

        Assert.DoesNotContain(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Items", StringComparison.Ordinal));
    }

    [Fact]
    public void ShortArray_StillReported()
    {
        var result = new NullableLengthModelValidator()
            .Validate(new NullableLengthModel { Tenant = "abc", Items = [1] });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Items", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidModel_IsValid()
    {
        var result = new NullableLengthModelValidator()
            .Validate(new NullableLengthModel { Tenant = "acme", Region = "eu", Code = "abc", Items = [1, 2] });

        Assert.True(result.IsValid);
    }
}
