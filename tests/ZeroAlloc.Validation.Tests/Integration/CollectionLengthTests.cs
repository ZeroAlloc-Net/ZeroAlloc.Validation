using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// <c>[MinLength]</c>, <c>[MaxLength]</c> and <c>[Length]</c> on a collection count its items
/// through <c>Count</c>, #369. A null collection passes, as a null string does. An array keeps
/// its Length, and its default message counts items too.
/// </summary>
public class CollectionLengthTests
{
    private readonly CollectionLengthModelValidator _validator = new();

    [Fact]
    public void CollectionsWithinTheirBounds_Pass()
    {
        var model = new CollectionLengthModel
        {
            Tags = ["a", "b"],
            Scores = [1],
            Limits = new Dictionary<string, int>(System.StringComparer.Ordinal) { ["x"] = 1, ["y"] = 2 },
        };

        Assert.True(_validator.Validate(model).IsValid);
    }

    [Fact]
    public void NullCollections_Pass()
    {
        Assert.True(_validator.Validate(new CollectionLengthModel()).IsValid);
    }

    [Fact]
    public void CollectionsOutsideTheirBounds_FailWithItemMessages()
    {
        var model = new CollectionLengthModel
        {
            Tags = ["a", "b", "c"],
            Scores = [],
            Limits = new Dictionary<string, int>(System.StringComparer.Ordinal),
            Codes = [1, 2, 3],
        };

        var messages = _validator.Validate(model).Failures.ToArray()
            .ToDictionary(f => f.PropertyName, f => f.ErrorMessage, System.StringComparer.Ordinal);

        Assert.Equal("Tags must contain at most 2 items.", messages["Tags"]);
        Assert.Equal("Scores must contain at least 1 item.", messages["Scores"]);
        Assert.Equal("Limits must contain between 1 and 2 items.", messages["Limits"]);
        Assert.Equal("Codes must contain at most 2 items.", messages["Codes"]);
    }
}
