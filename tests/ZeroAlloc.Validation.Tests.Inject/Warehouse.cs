using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

// A validator that composes others, issue #246: a nested [Validate] property, a collection of
// [Validate] elements and a [ValidateWith] property. Site adds one level of nesting above it.
[Validate]
public class Warehouse
{
    [NotEmpty] public string Name { get; set; } = "";

    public ApiKeyOptions? Primary { get; set; }

    public IList<ApiKeyOptions> Keys { get; set; } = new List<ApiKeyOptions>();

    [ValidateWith(typeof(DockValidator))]
    public Dock? Dock { get; set; }
}
