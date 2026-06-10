using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

// B3 regression coverage: a [Validate]-decorated readonly record struct included
// as IReadOnlyList<OrderTag> on the parent Order ensures the generator's
// NeedsNullGuard(ITypeSymbol) predicate correctly omits the `is not null` guard
// around value-type elements. Without that predicate, the smoke build fails
// with CS0037 (cannot convert null to a non-nullable value type).
[Validate]
public readonly record struct OrderTag(
    [property: NotEmpty(Message = "Tag is required.")] string Label);
