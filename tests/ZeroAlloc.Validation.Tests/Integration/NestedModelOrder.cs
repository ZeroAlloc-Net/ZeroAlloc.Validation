using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

// Issue #207: a top-level model composing validators of models nested in other types, as a
// member, as a collection element and through a nested hand-written [ValidateWith] validator.
[Validate]
public sealed class NestedModelOrder
{
    public NestedModelHost.Request Customer { get; init; } = new();

    public IReadOnlyList<NestedModelLevel1.Level2.Request> Lines { get; init; } = [];

    [ValidateWith(typeof(NestedModelHost.ReferenceValidator))]
    public NestedModelHost.Reference Reference { get; init; } = new();
}
