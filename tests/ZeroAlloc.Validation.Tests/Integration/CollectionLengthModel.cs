using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// The length rules on collections, #369: each compares the collection's <c>Count</c>, and its
/// default message counts items instead of characters.
/// </summary>
[Validate]
public class CollectionLengthModel
{
    [MaxLength(2)]
    public IList<string>? Tags { get; set; }

    [MinLength(1)]
    public IReadOnlyCollection<int>? Scores { get; set; }

    [Length(1, 2)]
    public IDictionary<string, int>? Limits { get; set; }

    [MaxLength(2)]
    public int[]? Codes { get; set; }
}
