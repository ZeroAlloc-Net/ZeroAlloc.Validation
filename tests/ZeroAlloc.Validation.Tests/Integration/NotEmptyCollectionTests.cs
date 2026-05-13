using System;
using System.Collections.Generic;
using Xunit;
using ZeroAlloc.Validation;

#pragma warning disable MA0048 // multiple types intentionally co-located with the test class
#pragma warning disable MA0016 // concrete collection types are intentional — we're testing per-type emission
#pragma warning disable MA0002 // string Dictionary comparer is irrelevant for these tests
#pragma warning disable CA1812 // model types are instantiated by the test framework

namespace ZeroAlloc.Validation.Tests.Integration;

// [NotEmpty] historically only emitted a string.IsNullOrEmpty(...) check, so
// applying it to any non-string type produced uncompilable code or silently
// wrong behavior. These tests pin the broadened type-aware behavior across
// the common .NET "has an empty notion" types.

[Validate]
public sealed class StringModel
{
    [NotEmpty] public string Name { get; set; } = "";
}

[Validate]
public sealed class ArrayModel
{
    [NotEmpty] public int[] Items { get; set; } = Array.Empty<int>();
}

[Validate]
public sealed class ListModel
{
    [NotEmpty] public List<int> Items { get; set; } = new();
}

[Validate]
public sealed class IReadOnlyListModel
{
    [NotEmpty] public IReadOnlyList<int> Items { get; set; } = Array.Empty<int>();
}

[Validate]
public sealed class HashSetModel
{
    [NotEmpty] public HashSet<int> Items { get; set; } = new();
}

[Validate]
public sealed class DictionaryModel
{
    [NotEmpty] public Dictionary<string, int> Items { get; set; } = new();
}

[Validate]
public sealed class GuidModel
{
    [NotEmpty] public Guid Id { get; set; }
}

[Validate]
public sealed class NullableGuidModel
{
    [NotEmpty] public Guid? Id { get; set; }
}

public class NotEmptyCollectionTests
{
    [Fact]
    public void String_EmptyValue_Fails()
    {
        var r = new StringModelValidator().Validate(new StringModel { Name = "" });
        Assert.False(r.IsValid);
        Assert.Equal("Name", r.Failures[0].PropertyName);
    }

    [Fact]
    public void String_PopulatedValue_Passes()
    {
        var r = new StringModelValidator().Validate(new StringModel { Name = "Alice" });
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Array_Empty_Fails()
    {
        var r = new ArrayModelValidator().Validate(new ArrayModel { Items = Array.Empty<int>() });
        Assert.False(r.IsValid);
        Assert.Equal("Items", r.Failures[0].PropertyName);
    }

    [Fact]
    public void Array_Populated_Passes()
    {
        var r = new ArrayModelValidator().Validate(new ArrayModel { Items = new[] { 1, 2 } });
        Assert.True(r.IsValid);
    }

    [Fact]
    public void List_Empty_Fails()
    {
        var r = new ListModelValidator().Validate(new ListModel { Items = new List<int>() });
        Assert.False(r.IsValid);
        Assert.Equal("Items", r.Failures[0].PropertyName);
    }

    [Fact]
    public void List_Populated_Passes()
    {
        var r = new ListModelValidator().Validate(new ListModel { Items = new List<int> { 42 } });
        Assert.True(r.IsValid);
    }

    [Fact]
    public void IReadOnlyList_Empty_Fails()
    {
        var r = new IReadOnlyListModelValidator().Validate(new IReadOnlyListModel { Items = Array.Empty<int>() });
        Assert.False(r.IsValid);
        Assert.Equal("Items", r.Failures[0].PropertyName);
    }

    [Fact]
    public void IReadOnlyList_Populated_Passes()
    {
        var r = new IReadOnlyListModelValidator().Validate(new IReadOnlyListModel { Items = new[] { 1 } });
        Assert.True(r.IsValid);
    }

    [Fact]
    public void HashSet_Empty_Fails()
    {
        var r = new HashSetModelValidator().Validate(new HashSetModel { Items = new HashSet<int>() });
        Assert.False(r.IsValid);
    }

    [Fact]
    public void HashSet_Populated_Passes()
    {
        var r = new HashSetModelValidator().Validate(new HashSetModel { Items = new HashSet<int> { 1, 2 } });
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Dictionary_Empty_Fails()
    {
        var r = new DictionaryModelValidator().Validate(new DictionaryModel { Items = new Dictionary<string, int>() });
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Dictionary_Populated_Passes()
    {
        var r = new DictionaryModelValidator().Validate(new DictionaryModel { Items = new Dictionary<string, int> { ["k"] = 1 } });
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Guid_Empty_Fails()
    {
        var r = new GuidModelValidator().Validate(new GuidModel { Id = Guid.Empty });
        Assert.False(r.IsValid);
        Assert.Equal("Id", r.Failures[0].PropertyName);
    }

    [Fact]
    public void Guid_Populated_Passes()
    {
        var r = new GuidModelValidator().Validate(new GuidModel { Id = Guid.NewGuid() });
        Assert.True(r.IsValid);
    }

    [Fact]
    public void NullableGuid_NullOrEmpty_Fails()
    {
        var r1 = new NullableGuidModelValidator().Validate(new NullableGuidModel { Id = null });
        Assert.False(r1.IsValid);

        var r2 = new NullableGuidModelValidator().Validate(new NullableGuidModel { Id = Guid.Empty });
        Assert.False(r2.IsValid);
    }

    [Fact]
    public void NullableGuid_Populated_Passes()
    {
        var r = new NullableGuidModelValidator().Validate(new NullableGuidModel { Id = Guid.NewGuid() });
        Assert.True(r.IsValid);
    }
}
