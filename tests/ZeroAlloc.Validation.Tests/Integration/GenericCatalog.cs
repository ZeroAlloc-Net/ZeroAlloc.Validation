using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A non-generic model holding closings of generic models, issue #238.</summary>
[Validate]
public class GenericCatalog
{
    [NotEmpty]
    public string Name { get; set; } = "";

    public GenericPage<GenericProduct> Page { get; set; } = new();

    public GenericMeasure<decimal> Price { get; set; } = new();

    public IReadOnlyList<GenericMeasure<int>> Counts { get; set; } = [];
}
