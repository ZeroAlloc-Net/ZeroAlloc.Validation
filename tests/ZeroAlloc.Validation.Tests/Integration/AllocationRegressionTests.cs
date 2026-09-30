using System;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Pins the allocation behaviour the library promises, which no other test covers: the valid path
/// must allocate nothing at all, and a failing validation must cost the result array and nothing
/// else. Measured with <see cref="GC.GetAllocatedBytesForCurrentThread"/> after warm-up, so these
/// catch a generator change that reintroduces a scratch allocation even when behaviour is correct.
/// </summary>
[Collection("Allocation")]
public class AllocationRegressionTests
{
    // 24-byte array header + one 32-byte ValidationFailure. Asserted as a ceiling rather than an
    // equality so a runtime with different header padding does not fail the suite spuriously.
    private const long OneFailureCeiling = 64;

    [Fact]
    public void FlatModel_ValidPath_AllocatesNothing()
    {
        var validator = new AllocFlatModelValidator();
        var model = new AllocFlatModel { Name = "ok", Age = 30 };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void FlatModel_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocFlatModelValidator();
        var model = new AllocFlatModel { Name = "", Age = 30 };

        Assert.InRange(Measure(() => validator.Validate(model).IsValid), 1, OneFailureCeiling);
    }

    [Fact]
    public void FailFastModel_ValidPath_AllocatesNothing()
    {
        var validator = new AllocFailFastModelValidator();
        var model = new AllocFailFastModel { Name = "ok", Age = 30 };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void FailFastModel_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocFailFastModelValidator();
        var model = new AllocFailFastModel { Name = "", Age = 30 };

        Assert.InRange(Measure(() => validator.Validate(model).IsValid), 1, OneFailureCeiling);
    }

    [Fact]
    public void GenericValueTypeClosing_ValidPath_AllocatesNothing()
    {
        // Issue #238: an INumber<T> comparison is double.CreateChecked, a constrained call that
        // does not box the value-type closing.
        var validator = new GenericMeasureValidator<int>();
        var model = new GenericMeasure<int> { Amount = 5, Optional = 50 };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void GenericValueTypeClosing_OfDecimal_ValidPath_AllocatesNothing()
    {
        var validator = new GenericMeasureValidator<decimal>();
        var model = new GenericMeasure<decimal> { Amount = 5.5m, Optional = 99.5m };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void GenericReferenceTypeClosing_WithNestedClosings_ValidPath_AllocatesNothing()
    {
        var validator = new GenericPageValidator<GenericProduct>(
            new GenericLineValidator<GenericProduct>(),
            new GenericLineValidator<GenericProduct>());
        var product = new GenericProduct();
        var model = new GenericPage<GenericProduct>
        {
            Title = "t",
            Selected = product,
            Featured = new GenericLine<GenericProduct> { Item = product },
            Lines = [new GenericLine<GenericProduct> { Item = product }, new GenericLine<GenericProduct> { Item = product }],
        };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void GenericEnumClosing_ValidPath_AllocatesNothing()
    {
        // Enum.IsDefined<T> is compiled per closing, without the boxing of Enum.IsDefined(Type, object).
        var validator = new GenericKindValidator<DayOfWeek>();
        var model = new GenericKind<DayOfWeek>(DayOfWeek.Friday, DayOfWeek.Monday);

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void NestedModel_ValidPath_AllocatesNothing()
    {
        var validator = new AllocParentModelValidator(new AllocChildModelValidator());
        var model = new AllocParentModel { Name = "ok", Child = new AllocChildModel { Code = "ok" } };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void NestedModel_ChildFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocParentModelValidator(new AllocChildModelValidator());
        var model = new AllocParentModel { Name = "ok", Child = new AllocChildModel { Code = "" } };

        // One failure surfaces from the child and is re-wrapped by the parent, so the child's
        // result array and the parent's are both in play.
        Assert.InRange(Measure(() => validator.Validate(model).IsValid), 1, OneFailureCeiling * 3);
    }

    [Fact]
    public void ArrayCollection_ValidPath_AllocatesNothing()
    {
        var validator = new AllocCollectionModelValidator(new AllocChildModelValidator());
        var model = new AllocCollectionModel
        {
            Name = "ok",
            Items = [new() { Code = "a" }, new() { Code = "b" }, new() { Code = "c" }]
        };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void CustomRuleModel_ValidPath_AllocatesNothing()
    {
        var validator = new AllocCustomRuleModelValidator();
        var model = new AllocCustomRuleModel { Name = "ok", Title = "one two three" };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void CustomRuleModel_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocCustomRuleModelValidator();
        var model = new AllocCustomRuleModel { Name = " ", Title = "one two three" };

        Assert.InRange(Measure(() => validator.Validate(model).IsValid), 1, OneFailureCeiling);
    }

    [Fact]
    public void InheritedRules_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocDerivedModelValidator();
        var model = new AllocDerivedModel { Name = "", Code = "ok" };

        Assert.InRange(Measure(() => validator.Validate(model).IsValid), 1, OneFailureCeiling);
    }

    // An async rule whose ValueTask completes at once leaves the generated async method to finish
    // synchronously, so its state machine stays on the stack and the valid path allocates nothing.
    [Fact]
    public void AsyncRuleModel_SynchronouslyCompletingValidPath_AllocatesNothing()
    {
        var validator = new AllocAsyncRuleModelValidator();
        var model = new AllocAsyncRuleModel { Name = "ok" };

        Assert.Equal(0, Measure(() => validator.ValidateAsync(model).Result.IsValid));
    }

    [Fact]
    public void AsyncRuleModel_SynchronouslyCompletingSingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocAsyncRuleModelValidator();
        var model = new AllocAsyncRuleModel { Name = "" };

        // [NotEmpty] fails; the async rule then fails too, so two failures: 24 + 2 * 32 bytes.
        Assert.InRange(Measure(() => validator.ValidateAsync(model).Result.IsValid), 1, OneFailureCeiling + 32);
    }

    // A behavior on a model with a nested validator: the chain reads the validator's fields, so
    // its lambdas cannot be static, and it caches their delegates instead of allocating them on
    // every call, issue #298.
    [Fact]
    public void BehaviorWithNestedValidator_Sync_ValidPath_AllocatesNothing()
    {
        NestedSyncAuditBehavior.CallLog = null;
        var validator = new PipelineNestedOrderValidator(new PipelineNestedLineValidator(), new PipelineNestedLineValidator());
        var model = new PipelineNestedOrder
        {
            Reference = "REF-001",
            Line = new PipelineNestedLine { Sku = "A" },
            Lines = [new PipelineNestedLine { Sku = "B" }],
        };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void BehaviorWithNestedValidator_Async_ValidPath_AllocatesNothing()
    {
        NestedAsyncAuditBehavior.CallLog = null;
        var validator = new PipelineNestedOrderValidator(new PipelineNestedLineValidator(), new PipelineNestedLineValidator());
        var model = new PipelineNestedOrder
        {
            Reference = "REF-001",
            Line = new PipelineNestedLine { Sku = "A" },
            Lines = [new PipelineNestedLine { Sku = "B" }],
        };

        Assert.Equal(0, Measure(() => validator.ValidateAsync(model).Result.IsValid));
    }

    [Fact]
    public void BehaviorWithNestedValidator_AsyncRuleModel_SynchronouslyCompletingValidPath_AllocatesNothing()
    {
        var validator = new AllocBehaviorAsyncRuleModelValidator(new PipelineNestedLineValidator());
        var model = new AllocBehaviorAsyncRuleModel { Name = "ok", Line = new PipelineNestedLine { Sku = "A" } };

        Assert.Equal(0, Measure(() => validator.ValidateAsync(model).Result.IsValid));
    }

    [Fact]
    public void BehaviorWithNestedValidator_GenericModel_Sync_ValidPath_AllocatesNothing()
    {
        var validator = new AllocGenericBehaviorModelValidator<string>(new PipelineNestedLineValidator());
        var model = new AllocGenericBehaviorModel<string> { Reference = "r", Line = new PipelineNestedLine { Sku = "A" } };

        Assert.Equal(0, Measure(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void BehaviorWithNestedValidator_GenericModel_Async_ValidPath_AllocatesNothing()
    {
        var validator = new AllocGenericBehaviorModelValidator<int>(new PipelineNestedLineValidator());
        var model = new AllocGenericBehaviorModel<int> { Reference = "r", Line = new PipelineNestedLine { Sku = "A" } };

        Assert.Equal(0, Measure(() => validator.ValidateAsync(model).Result.IsValid));
    }

    /// <summary>Bytes allocated per call, averaged over a warmed-up run.</summary>
    private static long Measure(Func<bool> action)
    {
        const int Warmup = 10_000;
        const int Iterations = 10_000;

        for (int i = 0; i < Warmup; i++) action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / Iterations;
    }
}
