using System;
using System.Linq;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

public class InheritanceTests
{
    [Fact]
    public void DerivedValidator_ValidatesInheritedProperty()
    {
        var result = new InheritanceDerivedModelValidator()
            .Validate(new InheritanceDerivedModel { Name = null, Quantity = 1 });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Name", StringComparison.Ordinal));
    }

    [Fact]
    public void DerivedValidator_StillValidatesOwnProperty()
    {
        var result = new InheritanceDerivedModelValidator()
            .Validate(new InheritanceDerivedModel { Name = "ok", Quantity = 0 });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Quantity", StringComparison.Ordinal));
    }

    [Fact]
    public void DerivedValidator_AllRulesSatisfied_IsValid()
    {
        var result = new InheritanceDerivedModelValidator()
            .Validate(new InheritanceDerivedModel { Name = "ok", Quantity = 1 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void InheritedFailures_AreReportedBeforeDerivedFailures()
    {
        var result = new InheritanceDerivedModelValidator()
            .Validate(new InheritanceDerivedModel { Name = null, Quantity = 0 });

        var names = result.Failures.ToArray().Select(f => f.PropertyName).ToArray();
        Assert.Equal(["Name", "Quantity"], names);
    }

    [Fact]
    public void BaseWithoutValidateAttribute_RulesStillInherited()
    {
        var result = new InheritanceAuditedOrderValidator()
            .Validate(new InheritanceAuditedOrder { ModifiedBy = null, Total = 5 });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "ModifiedBy", StringComparison.Ordinal));
    }

    [Fact]
    public void IncludeBasePropertiesFalse_SkipsInheritedRules()
    {
        var result = new InheritanceOptOutModelValidator()
            .Validate(new InheritanceOptOutModel { Name = null, Quantity = 1 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void IncludeBasePropertiesFalse_StillValidatesOwnRules()
    {
        var result = new InheritanceOptOutModelValidator()
            .Validate(new InheritanceOptOutModel { Name = null, Quantity = 0 });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Quantity", StringComparison.Ordinal));
    }

    [Fact]
    public void MultiLevelHierarchy_CollectsRulesFromEveryLevel()
    {
        var result = new InheritanceGrandchildModelValidator()
            .Validate(new InheritanceGrandchildModel { Name = null, Quantity = 0, Sku = "" });

        var names = result.Failures.ToArray().Select(f => f.PropertyName).ToArray();
        Assert.Equal(["Name", "Quantity", "Sku"], names);
    }

    [Fact]
    public void OverriddenProperty_UsesMostDerivedRules()
    {
        // Base requires MinLength(5); the override relaxes it to MinLength(2).
        // The most-derived declaration wins, so a 3-character value is valid.
        var result = new InheritanceShadowDerivedValidator()
            .Validate(new InheritanceShadowDerived { Code = "abc" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void OverriddenProperty_MostDerivedRuleStillEnforced()
    {
        var result = new InheritanceShadowDerivedValidator()
            .Validate(new InheritanceShadowDerived { Code = "a" });

#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        Assert.Single(result.Failures.ToArray());
#pragma warning restore HLQ005
    }

    [Fact]
    public void CustomValidationMethodOnBase_IsInvoked()
    {
        var result = new InheritanceCustomDerivedValidator()
            .Validate(new InheritanceCustomDerived { Owner = "ops", Budget = -1 });

        Assert.Contains(result.Failures.ToArray(),
            f => string.Equals(f.PropertyName, "Budget", StringComparison.Ordinal));
    }
}
