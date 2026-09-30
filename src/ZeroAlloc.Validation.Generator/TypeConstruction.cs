using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// One type a generated validator walks, as one model walks it: the type with its type
/// arguments, the model to find it from, and the properties the model reads from it.
/// </summary>
/// <param name="Construction">The type as the model walks it, type arguments included.</param>
/// <param name="ResolverModel">The metadata name of a model that walks it, to find it from.</param>
/// <param name="AsModel">Whether the model is the type itself rather than a type deriving from it.</param>
/// <param name="RuleProperties">
/// The properties whose rules the validator reads: those
/// <see cref="MemberWalker.GetMembersIncludingBase"/> yields from this type.
/// </param>
/// <param name="CheckedProperties">
/// The properties no more-derived declaration hides, which ZV0027 checks for a rule the validator
/// cannot read.
/// </param>
internal sealed record TypeConstruction(
    string Construction,
    string ResolverModel,
    bool AsModel,
    EquatableArray<string> RuleProperties,
    EquatableArray<string> CheckedProperties);
