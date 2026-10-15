using System;
using System.Linq;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// <c>[StopOnFirstFailure]</c> on a class applies the per-property cascade to every property, so
/// it does not have to be repeated. Paired with <c>[Validate(StopOnFirstFailure = true)]</c> the
/// model reports the first failing rule of the first failing property and stops there.
/// </summary>
[Collection(AllocationCollection.Name)]
public class ClassLevelCascadeTests
{
    private const int Iterations = 10_000;

    [Fact]
    public void ClassCascadeWithModelStop_ReportsOnlyTheFirstFailingRule()
    {
        // Tenant violates both NotEmpty and MinLength, and User is invalid too.
        var result = new ClassCascadeModelValidator()
            .Validate(new ClassCascadeModel { Tenant = "", User = "" });

        Assert.Collection(result.Failures.ToArray(), failure =>
        {
            Assert.Equal("Tenant", failure.PropertyName);
            Assert.Equal("Tenant must not be empty.", failure.ErrorMessage);
        });
    }

    [Fact]
    public void ClassCascadeWithModelStop_LaterRuleReportedWhenEarlierPasses()
    {
        // "ab" passes NotEmpty but violates MinLength(3).
        var result = new ClassCascadeModelValidator()
            .Validate(new ClassCascadeModel { Tenant = "ab", User = "ok" });

        Assert.Collection(result.Failures.ToArray(),
            failure => Assert.Equal("Tenant must be at least 3 characters.", failure.ErrorMessage));
    }

    [Fact]
    public void ClassCascadeWithModelStop_ValidModelIsValid()
    {
        var result = new ClassCascadeModelValidator()
            .Validate(new ClassCascadeModel { Tenant = "acme", User = "root" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ClassCascadeAlone_ReportsOneFailurePerProperty()
    {
        // Without model-level fail-fast every property is still evaluated, but each contributes
        // at most one failure rather than every rule it violates.
        var result = new ClassCascadeOnlyModelValidator()
            .Validate(new ClassCascadeOnlyModel { Tenant = "", User = "" });

        var names = result.Failures.ToArray().Select(f => f.PropertyName).ToArray();
        Assert.Equal(["Tenant", "User"], names);
    }

    [Fact]
    public void WithoutClassCascade_EveryViolatedRuleIsReported()
    {
        // The unchanged default: a property reports every rule it violates.
        var result = new ClassCascadeControlModelValidator()
            .Validate(new ClassCascadeControlModel { Tenant = "" });

        Assert.Equal(2, result.Failures.ToArray().Length);
    }

    [Fact]
    public void ClassCascade_AppliesToInheritedRules()
    {
        var result = new ClassCascadeInheritedModelValidator()
            .Validate(new ClassCascadeInheritedModel { Inherited = "", Own = "" });

        var names = result.Failures.ToArray().Select(f => f.PropertyName).ToArray();
        Assert.Equal(["Inherited", "Own"], names);
    }

    [Fact]
    public void ClassCascadeWithModelStop_AllocatesResultArrayOnly()
    {
        var validator = new ClassCascadeModelValidator();
        var model = new ClassCascadeModel { Tenant = "", User = "" };

        // One possible failure per property means the direct return applies: no scratch buffer.
        // The result array alone is 56 B: a 24-byte array header and one 32-byte ValidationFailure.
        Assert.Equal(56, AllocationGate.MeasureBytesPerCall(Iterations, () => validator.Validate(model)));
    }
}
