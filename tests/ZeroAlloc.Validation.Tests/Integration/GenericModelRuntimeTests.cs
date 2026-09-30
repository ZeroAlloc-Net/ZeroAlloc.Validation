using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Generic <c>[Validate]</c> models at run time, issue #238: one generated validator per model,
/// generic over its type parameters, validates every closing, value-type and reference-type alike.
/// </summary>
public class GenericModelRuntimeTests
{
    [Fact]
    public void ValueTypeClosing_ComparesTheNumber()
    {
        var validator = new GenericMeasureValidator<int>();

        Assert.True(validator.Validate(new GenericMeasure<int> { Amount = 5 }).IsValid);

        var result = validator.Validate(new GenericMeasure<int> { Amount = 0, Optional = 101 });
        Assert.Equal(["Amount", "Optional"], Names(result));
        Assert.Equal("Amount was 0.", result.Failures[0].ErrorMessage);
    }

    [Fact]
    public void ValueTypeClosing_OfAnotherNumber_UsesTheSameValidator()
    {
        var validator = new GenericMeasureValidator<decimal>();

        Assert.True(validator.Validate(new GenericMeasure<decimal> { Amount = 0.5m, Optional = 1m }).IsValid);
        var result = validator.Validate(new GenericMeasure<decimal> { Amount = -1.25m, Optional = 0.5m, Unit = "" });
        Assert.Equal(["Amount", "Optional", "Unit"], Names(result));
        Assert.Equal("Amount was -1.25.", result.Failures[0].ErrorMessage);
    }

    [Fact]
    public void NullableTypeParameterValue_PassesAComparisonWhenMissing()
    {
        var result = new GenericMeasureValidator<long>().Validate(new GenericMeasure<long> { Amount = 1, Optional = null });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ReferenceTypeClosing_ComposesItsNestedAndCollectionClosings()
    {
        var validator = new GenericPageValidator<GenericProduct>(
            new GenericLineValidator<GenericProduct>(),
            new GenericLineValidator<GenericProduct>());
        var page = new GenericPage<GenericProduct>
        {
            Title = "",
            Selected = null,
            Featured = new GenericLine<GenericProduct> { Quantity = 0, Item = new GenericProduct() },
            Lines =
            [
                new GenericLine<GenericProduct> { Item = new GenericProduct() },
                new GenericLine<GenericProduct> { Item = null },
            ],
        };

        var result = validator.Validate(page);

        Assert.Equal(["Title", "Selected", "Featured.Quantity", "Lines[1].Item"], Names(result));
    }

    [Fact]
    public void NullableReferenceTypeClosing_TestsForNull()
    {
        var validator = new GenericSlotValidator<string?>();

        Assert.True(validator.Validate(new GenericSlot<string?> { Value = "a", Checked = null, Forbidden = "x", Marker = "m" }).IsValid);
        var result = validator.Validate(new GenericSlot<string?> { Value = null, Checked = "x", Forbidden = "x", Marker = null, Label = "bad" });
        Assert.Equal(["Value", "Checked", "Marker", "Label"], Names(result));
        Assert.Equal("Marker must not be the default value.", result.Failures[2].ErrorMessage);
    }

    [Fact]
    public void ValueTypeClosing_OfAnUnconstrainedModel_NeverFailsTheNullRule()
    {
        var validator = new GenericSlotValidator<int>();

        Assert.True(validator.Validate(new GenericSlot<int> { Value = 0, Checked = 1, Forbidden = 2, Marker = 7 }).IsValid);
        Assert.Equal(["Checked", "Marker"], Names(validator.Validate(new GenericSlot<int> { Checked = 2, Forbidden = 2, Marker = 0 })));
    }

    [Fact]
    public void EnumTypeParameter_IsCheckedAgainstTheClosingsMembers()
    {
        var validator = new GenericKindValidator<DayOfWeek>();

        Assert.True(validator.Validate(new GenericKind<DayOfWeek>(DayOfWeek.Monday, null)).IsValid);
        Assert.Equal(["Kind", "Fallback"], Names(validator.Validate(new GenericKind<DayOfWeek>((DayOfWeek)42, (DayOfWeek)43))));
    }

    [Fact]
    public void NonGenericModel_ComposesTheClosingsItHolds()
    {
        var validator = new GenericCatalogValidator(
            new GenericPageValidator<GenericProduct>(new GenericLineValidator<GenericProduct>(), new GenericLineValidator<GenericProduct>()),
            new GenericMeasureValidator<decimal>(),
            new GenericMeasureValidator<int>());
        var catalog = new GenericCatalog
        {
            Name = "c",
            Page = new GenericPage<GenericProduct> { Title = "t", Selected = new GenericProduct() },
            Price = new GenericMeasure<decimal> { Amount = 0m },
            Counts = [new GenericMeasure<int> { Amount = 3 }, new GenericMeasure<int> { Amount = -3 }],
        };

        Assert.Equal(["Price.Amount", "Counts[1].Amount"], Names(validator.Validate(catalog)));
    }

    [Fact]
    public void BehaviorOnTheOpenForm_RunsForEveryClosing()
    {
        GenericAuditBehavior.CallLog = [];

        Assert.False(new GenericAuditedValidator<int>(new GenericLineValidator<GenericProduct>()).Validate(new GenericAudited<int>()).IsValid);
        Assert.True(new GenericAuditedValidator<string>(new GenericLineValidator<GenericProduct>())
            .Validate(new GenericAudited<string> { Reference = "r" }).IsValid);

        Assert.Equal(["GenericAudited`1", "GenericAudited`1"], GenericAuditBehavior.CallLog, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AsynchronousRuleOnAGenericModel_IsAwaited()
    {
        var validator = new GenericAsyncBoxValidator<int>();

        Assert.Throws<NotSupportedException>(() => validator.Validate(new GenericAsyncBox<int>()));
        var result = await validator.ValidateAsync(new GenericAsyncBox<int> { Name = "taken-1" });
        Assert.Equal(["Name"], Names(result));
    }

    [Fact]
    public async Task ModelValidator_ValidatesTheModelPassedAsObject()
    {
        IModelValidator validator = new GenericMeasureValidator<int>();

        Assert.Equal(typeof(GenericMeasure<int>), validator.ModelType);
        var result = await validator.ValidateAsync(new GenericMeasure<int> { Amount = -1 }, default);
        Assert.Equal(["Amount"], Names(result));
        await Assert.ThrowsAsync<InvalidCastException>(() => validator.ValidateAsync(new GenericMeasure<long>(), default).AsTask());
    }

    private static List<string> Names(ValidationResult result) =>
        result.Failures.ToArray().Select(f => f.PropertyName).ToList();
}
