using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class NestedAsyncRuleTests
{
    private readonly AsyncRuleParentValidator _validator = new(
        new AsyncRuleChildValidator(), new AsyncRuleChildValidator(), new AsyncRuleChildValidator());

    [Fact]
    public async Task Nested_and_collection_async_rules_are_awaited_and_prefixed()
    {
        var model = new AsyncRuleParent
        {
            Title = "",
            Child = new AsyncRuleChild { Name = "taken-child" },
            Children = new List<AsyncRuleChild> { new() { Name = "free" }, new() { Name = "taken-list" } },
            Others = new[] { new AsyncRuleChild { Name = "taken-array" } },
        };

        var failures = (await _validator.ValidateAsync(model)).Failures.ToArray();

        Assert.Equal(
            new[] { "Title", "Child.Name", "Children[1].Name", "Others[0].Name" },
            Array.ConvertAll(failures, f => f.PropertyName),
            StringComparer.Ordinal);
        Assert.Equal("Name 'taken-list' is taken.", failures[2].ErrorMessage);
        Assert.Equal("TAKEN", failures[2].ErrorCode);
    }

    [Fact]
    public async Task Null_nested_and_collection_properties_are_skipped()
    {
        ValidationAssert.NoErrors(await _validator.ValidateAsync(new AsyncRuleParent { Title = "t" }));
    }

    [Fact]
    public async Task Null_collection_elements_are_skipped()
    {
        var model = new AsyncRuleParent { Title = "t", Children = new List<AsyncRuleChild> { null!, new() { Name = "taken" } } };

        ValidationAssert.HasError(await _validator.ValidateAsync(model), "Children[1].Name");
    }

    [Fact]
    public void Sync_Validate_of_a_parent_with_async_nested_rules_throws()
    {
        Assert.Throws<NotSupportedException>(() => _validator.Validate(new AsyncRuleParent { Title = "t" }));
    }
}
