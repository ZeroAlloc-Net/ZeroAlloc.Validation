namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Why the generated validator cannot read a property as <c>instance.Prop</c>; see
/// <see cref="MemberWalker.GetUnreadableReason"/>.
/// </summary>
internal enum UnreadableReason
{
    None,
    Static,
    Indexer,
    NoGetter,
    Inaccessible,
    GetterInaccessible,
}
