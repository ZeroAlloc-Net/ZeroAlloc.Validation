using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

// A generic model, issue #238. Nothing closed can be registered for it as declared; its closings
// are registered by the models composing them, such as Shelf.
[Validate]
public class Page<TItem> where TItem : class
{
    [NotEmpty] public string Title { get; set; } = "";

    public IList<Line<TItem>> Lines { get; set; } = new List<Line<TItem>>();
}
