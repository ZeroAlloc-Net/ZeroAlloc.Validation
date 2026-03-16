using System;
using System.Linq;
using Xunit;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public class EnrichedMetadataTests
{
    private readonly EnrichedMetadataModelValidator _validator = new();

    [Fact]
    public void ErrorCode_SetOnFailure_WhenSpecified()
    {
        var result = _validator.Validate(new EnrichedMetadataModel { Name = "", Age = 1, Bio = "" });
        var failure = result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Name", StringComparison.Ordinal));
        Assert.Equal("NAME_REQUIRED", failure.ErrorCode);
    }

    [Fact]
    public void Severity_Warning_SetOnFailure_WhenSpecified()
    {
        var result = _validator.Validate(new EnrichedMetadataModel { Name = "ok", Age = 0, Bio = "" });
        var failure = result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Age", StringComparison.Ordinal));
        Assert.Equal(Severity.Warning, failure.Severity);
        Assert.Equal("AGE_WARN", failure.ErrorCode);
    }

    [Fact]
    public void Severity_DefaultsToError_WhenNotSpecified()
    {
        var result = _validator.Validate(new EnrichedMetadataModel { Name = "", Age = 1, Bio = "" });
        var failure = result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Name", StringComparison.Ordinal));
        Assert.Equal(Severity.Error, failure.Severity);
    }

    [Fact]
    public void ErrorCode_Null_WhenNotSpecified()
    {
        var result = _validator.Validate(new EnrichedMetadataModel { Name = "ok", Age = 1, Bio = new string('x', 101) });
        var failure = result.Failures.ToArray().First(f => string.Equals(f.PropertyName, "Bio", StringComparison.Ordinal));
        Assert.Null(failure.ErrorCode);
    }
}
