using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A generic model closed over a reference type, with a nested and a collection closing, issue #238.</summary>
[Validate]
public class GenericPage<TItem> where TItem : class
{
    [NotEmpty]
    public string Title { get; set; } = "";

    [NotNull]
    public TItem? Selected { get; set; }

    public GenericLine<TItem>? Featured { get; set; }

    public IReadOnlyList<GenericLine<TItem>> Lines { get; set; } = [];
}
