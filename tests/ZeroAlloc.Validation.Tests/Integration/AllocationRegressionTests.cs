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
    public void InheritedRules_SingleFailure_AllocatesResultArrayOnly()
    {
        var validator = new AllocDerivedModelValidator();
        var model = new AllocDerivedModel { Name = "", Code = "ok" };

        Assert.InRange(Measure(() => validator.Validate(model).IsValid), 1, OneFailureCeiling);
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
