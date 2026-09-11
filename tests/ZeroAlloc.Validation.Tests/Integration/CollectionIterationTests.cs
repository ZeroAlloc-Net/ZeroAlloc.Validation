using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// A collection property is walked differently depending on how it is declared — a span for
/// <c>List&lt;T&gt;</c>, an indexer for <c>IList&lt;T&gt;</c> and <c>IReadOnlyList&lt;T&gt;</c>,
/// plain <c>foreach</c> otherwise — so each declaration has to report the same failures, with the
/// same indices, as every other.
/// </summary>
public class CollectionIterationTests
{
    private static AllocChildModel[] Items() =>
    [
        new() { Code = "ok" },
        new() { Code = "" },   // index 1 fails
        new() { Code = "ok" },
    ];

    [Fact]
    public void ArrayCollection_ReportsFailureWithIndex()
    {
        var result = new AllocCollectionModelValidator(new AllocChildModelValidator())
            .Validate(new AllocCollectionModel { Name = "ok", Items = Items() });

        AssertSingleIndexedFailure(result);
    }

    [Fact]
    public void ListCollection_ReportsFailureWithIndex()
    {
        var result = new AllocListCollectionModelValidator(new AllocChildModelValidator())
            .Validate(new AllocListCollectionModel { Name = "ok", Items = [.. Items()] });

        AssertSingleIndexedFailure(result);
    }

    [Fact]
    public void IListCollection_ReportsFailureWithIndex()
    {
        var result = new AllocIListCollectionModelValidator(new AllocChildModelValidator())
            .Validate(new AllocIListCollectionModel { Name = "ok", Items = [.. Items()] });

        AssertSingleIndexedFailure(result);
    }

    [Fact]
    public void ReadOnlyListCollection_ReportsFailureWithIndex()
    {
        var result = new AllocReadOnlyListCollectionModelValidator(new AllocChildModelValidator())
            .Validate(new AllocReadOnlyListCollectionModel { Name = "ok", Items = Items() });

        AssertSingleIndexedFailure(result);
    }

    [Fact]
    public void EnumerableCollection_ReportsFailureWithIndex()
    {
        var result = new AllocEnumerableCollectionModelValidator(new AllocChildModelValidator())
            .Validate(new AllocEnumerableCollectionModel { Name = "ok", Items = Items() });

        AssertSingleIndexedFailure(result);
    }

    [Fact]
    public void AllItemsFail_EveryIndexIsReportedInOrder()
    {
        AllocChildModel[] allBad = [new() { Code = "" }, new() { Code = "" }, new() { Code = "" }];

        var result = new AllocIListCollectionModelValidator(new AllocChildModelValidator())
            .Validate(new AllocIListCollectionModel { Name = "ok", Items = [.. allBad] });

        var names = result.Failures.ToArray().Select(f => f.PropertyName).ToArray();
        Assert.Equal(["Items[0].Code", "Items[1].Code", "Items[2].Code"], names);
    }

    [Fact]
    public void EmptyCollection_IsValid()
    {
        var result = new AllocIListCollectionModelValidator(new AllocChildModelValidator())
            .Validate(new AllocIListCollectionModel { Name = "ok", Items = [] });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullCollection_IsValid()
    {
        var result = new AllocIListCollectionModelValidator(new AllocChildModelValidator())
            .Validate(new AllocIListCollectionModel { Name = "ok", Items = null! });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void InterfaceCollection_ValidPath_AllocatesNothing()
    {
        // foreach over an interface-typed collection boxes its enumerator; indexing does not.
        var validator = new AllocIListCollectionModelValidator(new AllocChildModelValidator());
        var model = new AllocIListCollectionModel
        {
            Name = "ok",
            Items = [new() { Code = "a" }, new() { Code = "b" }, new() { Code = "c" }],
        };

        Assert.Equal(0, MeasureAllocation(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void ReadOnlyListCollection_ValidPath_AllocatesNothing()
    {
        var validator = new AllocReadOnlyListCollectionModelValidator(new AllocChildModelValidator());
        var model = new AllocReadOnlyListCollectionModel
        {
            Name = "ok",
            Items = [new() { Code = "a" }, new() { Code = "b" }, new() { Code = "c" }],
        };

        Assert.Equal(0, MeasureAllocation(() => validator.Validate(model).IsValid));
    }

    [Fact]
    public void ListCollection_ValidPath_AllocatesNothing()
    {
        var validator = new AllocListCollectionModelValidator(new AllocChildModelValidator());
        var model = new AllocListCollectionModel
        {
            Name = "ok",
            Items = [new() { Code = "a" }, new() { Code = "b" }, new() { Code = "c" }],
        };

        Assert.Equal(0, MeasureAllocation(() => validator.Validate(model).IsValid));
    }

    private static void AssertSingleIndexedFailure(ValidationResult result)
    {
        var failures = result.Failures.ToArray();
#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        var failure = Assert.Single(failures);
#pragma warning restore HLQ005
        Assert.Equal("Items[1].Code", failure.PropertyName);
    }

    private static long MeasureAllocation(Func<bool> action)
    {
        const int Warmup = 10_000;
        const int Iterations = 10_000;

        for (int i = 0; i < Warmup; i++) action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / Iterations;
    }
}
