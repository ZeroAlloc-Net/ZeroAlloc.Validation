using System;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Pins the allocation behaviour the library promises, which no other test covers: the valid path
/// must allocate nothing at all, and a failing validation must cost the result array and nothing
/// else. Measured with <see cref="AllocationGate"/>, so these catch a generator change that
/// reintroduces a scratch allocation even when behaviour is correct.
/// </summary>
[Collection(AllocationCollection.Name)]
public class AllocationRegressionTests
{
    private const int Iterations = 10_000;

    // The result array alone: a 24-byte array header and one 32-byte ValidationFailure struct, on
    // the 64-bit runtimes the suite runs on. Asserted exactly, so a scratch allocation of any size
    // on the failure path fails the test.
    private const long OneFailureArray = 56;

    [Fact]
    public void FlatModel_ValidPath_AllocatesNothing()
    {
        var validator = new AllocFlatModelValidator();
        var model = new AllocFlatModel { Name = "ok", Age = 30 };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "FlatModel_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void FlatModel_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocFlatModelValidator();
        var model = new AllocFlatModel { Name = "", Age = 30 };

        Assert.Equal(OneFailureArray, AllocationGate.MeasureBytesPerCall(Iterations, () => validator.Validate(model)));
    }

    [Fact]
    public void FailFastModel_ValidPath_AllocatesNothing()
    {
        var validator = new AllocFailFastModelValidator();
        var model = new AllocFailFastModel { Name = "ok", Age = 30 };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "FailFastModel_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void FailFastModel_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocFailFastModelValidator();
        var model = new AllocFailFastModel { Name = "", Age = 30 };

        Assert.Equal(OneFailureArray, AllocationGate.MeasureBytesPerCall(Iterations, () => validator.Validate(model)));
    }

    [Fact]
    public void GenericValueTypeClosing_ValidPath_AllocatesNothing()
    {
        // Issue #238: an INumber<T> comparison is double.CreateChecked, a constrained call that
        // does not box the value-type closing.
        var validator = new GenericMeasureValidator<int>();
        var model = new GenericMeasure<int> { Amount = 5, Optional = 50 };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "GenericValueTypeClosing_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void GenericValueTypeClosing_OfDecimal_ValidPath_AllocatesNothing()
    {
        var validator = new GenericMeasureValidator<decimal>();
        var model = new GenericMeasure<decimal> { Amount = 5.5m, Optional = 99.5m };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "GenericValueTypeClosing_OfDecimal_ValidPath_AllocatesNothing");
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

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "GenericReferenceTypeClosing_WithNestedClosings_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void GenericEnumClosing_ValidPath_AllocatesNothing()
    {
        // Enum.IsDefined<T> is compiled per closing, without the boxing of Enum.IsDefined(Type, object).
        var validator = new GenericKindValidator<DayOfWeek>();
        var model = new GenericKind<DayOfWeek>(DayOfWeek.Friday, DayOfWeek.Monday);

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "GenericEnumClosing_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void ConcreteEnum_ValidPath_AllocatesNothing()
    {
        // Issue #316: a concrete enum is checked with Enum.IsDefined<TEnum>, not the boxing
        // Enum.IsDefined(Type, object) overload, on the plain and the nullable property alike.
        var validator = new AllocEnumModelValidator();
        var model = new AllocEnumModel { Light = TrafficLight.Green, Optional = TrafficLight.Red };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "ConcreteEnum_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void ConcreteEnum_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocEnumModelValidator();
        var model = new AllocEnumModel { Light = (TrafficLight)42, Optional = null };

        Assert.Equal(OneFailureArray, AllocationGate.MeasureBytesPerCall(Iterations, () => validator.Validate(model)));
    }

    [Fact]
    public void PrecisionScale_ValidPath_AllocatesNothing()
    {
        // Issue #316: the scale is read without the int[4] that decimal.GetBits returns.
        var validator = new AllocPrecisionScaleModelValidator();
        var model = new AllocPrecisionScaleModel { Amount = 1234.56m, Optional = 0.5m };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "PrecisionScale_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void PrecisionScale_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocPrecisionScaleModelValidator();
        var model = new AllocPrecisionScaleModel { Amount = 1.234m, Optional = null };

        Assert.Equal(OneFailureArray, AllocationGate.MeasureBytesPerCall(Iterations, () => validator.Validate(model)));
    }

    [Fact]
    public void CodePointLength_ValidPath_AllocatesNothing()
    {
        // Issue #361: counting code points walks the string as a span. Each value is in the band
        // where Length alone cannot decide, so every call counts.
        var validator = new AllocCodePointLengthModelValidator();
        var model = new AllocCodePointLengthModel { Max = "\U0001F600\U0001F600ab", Range = "a\U0001F600\U0001F600" };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "CodePointLength_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void CodePointLength_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocCodePointLengthModelValidator();
        var model = new AllocCodePointLengthModel { Max = "\U0001F600\U0001F600abc", Range = "ab" };

        Assert.Equal(OneFailureArray, AllocationGate.MeasureBytesPerCall(Iterations, () => validator.Validate(model)));
    }

    [Fact]
    public void NestedModel_ValidPath_AllocatesNothing()
    {
        var validator = new AllocParentModelValidator(new AllocChildModelValidator());
        var model = new AllocParentModel { Name = "ok", Child = new AllocChildModel { Code = "ok" } };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "NestedModel_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void NestedModel_ChildFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocParentModelValidator(new AllocChildModelValidator());
        var model = new AllocParentModel { Name = "ok", Child = new AllocChildModel { Code = "" } };

        // One failure surfaces from the child and is re-wrapped by the parent, so the child's
        // result array and the parent's are both in play: 56 B each, plus 48 B for the prefixed
        // property name "Child.Code".
        Assert.Equal(2 * OneFailureArray + 48, AllocationGate.MeasureBytesPerCall(Iterations, () => validator.Validate(model)));
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

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "ArrayCollection_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void CustomRuleModel_ValidPath_AllocatesNothing()
    {
        var validator = new AllocCustomRuleModelValidator();
        var model = new AllocCustomRuleModel { Name = "ok", Title = "one two three" };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "CustomRuleModel_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void CustomRuleModel_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocCustomRuleModelValidator();
        var model = new AllocCustomRuleModel { Name = " ", Title = "one two three" };

        Assert.Equal(OneFailureArray, AllocationGate.MeasureBytesPerCall(Iterations, () => validator.Validate(model)));
    }

    [Fact]
    public void InheritedRules_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocDerivedModelValidator();
        var model = new AllocDerivedModel { Name = "", Code = "ok" };

        Assert.Equal(OneFailureArray, AllocationGate.MeasureBytesPerCall(Iterations, () => validator.Validate(model)));
    }

    // An async rule whose ValueTask completes at once leaves the generated async method to finish
    // synchronously, so its state machine stays on the stack and the valid path allocates nothing.
    [Fact]
    public void AsyncRuleModel_SynchronouslyCompletingValidPath_AllocatesNothing()
    {
        var validator = new AllocAsyncRuleModelValidator();
        var model = new AllocAsyncRuleModel { Name = "ok" };

        AllocationGate.AssertBudgetValueTask(0, Iterations, () => validator.ValidateAsync(model), "AsyncRuleModel_SynchronouslyCompletingValidPath_AllocatesNothing");
    }

    [Fact]
    public void AsyncRuleModel_SynchronouslyCompletingSingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocAsyncRuleModelValidator();
        var model = new AllocAsyncRuleModel { Name = "" };

        // [NotEmpty] fails; the async rule then fails too, so two failures: 24 + 2 * 32 = 88 bytes.
        Assert.Equal(OneFailureArray + 32, AllocationGate.MeasureBytesPerCallValueTask(Iterations, () => validator.ValidateAsync(model)));
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

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "BehaviorWithNestedValidator_Sync_ValidPath_AllocatesNothing");
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

        AllocationGate.AssertBudgetValueTask(0, Iterations, () => validator.ValidateAsync(model), "BehaviorWithNestedValidator_Async_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void BehaviorWithNestedValidator_AsyncRuleModel_SynchronouslyCompletingValidPath_AllocatesNothing()
    {
        var validator = new AllocBehaviorAsyncRuleModelValidator(new PipelineNestedLineValidator());
        var model = new AllocBehaviorAsyncRuleModel { Name = "ok", Line = new PipelineNestedLine { Sku = "A" } };

        AllocationGate.AssertBudgetValueTask(0, Iterations, () => validator.ValidateAsync(model), "BehaviorWithNestedValidator_AsyncRuleModel_SynchronouslyCompletingValidPath_AllocatesNothing");
    }

    [Fact]
    public void BehaviorWithNestedValidator_GenericModel_Sync_ValidPath_AllocatesNothing()
    {
        var validator = new AllocGenericBehaviorModelValidator<string>(new PipelineNestedLineValidator());
        var model = new AllocGenericBehaviorModel<string> { Reference = "r", Line = new PipelineNestedLine { Sku = "A" } };

        AllocationGate.AssertBudget(0, Iterations, () => validator.Validate(model), "BehaviorWithNestedValidator_GenericModel_Sync_ValidPath_AllocatesNothing");
    }

    [Fact]
    public void BehaviorWithNestedValidator_GenericModel_Async_ValidPath_AllocatesNothing()
    {
        var validator = new AllocGenericBehaviorModelValidator<int>(new PipelineNestedLineValidator());
        var model = new AllocGenericBehaviorModel<int> { Reference = "r", Line = new PipelineNestedLine { Sku = "A" } };

        AllocationGate.AssertBudgetValueTask(0, Iterations, () => validator.ValidateAsync(model), "BehaviorWithNestedValidator_GenericModel_Async_ValidPath_AllocatesNothing");
    }
}
