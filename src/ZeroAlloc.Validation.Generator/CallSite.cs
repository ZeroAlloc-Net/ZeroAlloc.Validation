using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// One call the generated validator makes for a rule: a <c>[Must]</c> predicate, a
/// <c>When</c> or <c>Unless</c> guard, a <c>[SkipWhen]</c> condition, a
/// <c>[CustomValidation]</c> method, or a custom rule's <c>IsValid</c>. A compiler warning
/// on it is mirrored as ZV0032 at <see cref="Attribute"/>.
/// </summary>
/// <param name="Text">The call exactly as the emitter writes it, such as <c>instance.Ok(instance.Code)</c>.</param>
/// <param name="Attribute">The attribute the call is made for, where ZV0032 is reported.</param>
/// <param name="Target">The member the attribute is applied to, the fallback location.</param>
/// <param name="DeclaringType">
/// The type declaring <paramref name="Target"/>, so a usage a <c>[Validate]</c> base type also
/// emits is reported once; <see langword="null"/> for <c>[SkipWhen]</c>, read from the model only.
/// </param>
/// <param name="Usage">How the message names the usage, such as <c>[Must] on 'Code'</c>.</param>
internal sealed record CallSite(string Text, AttributeData Attribute, ISymbol Target, INamedTypeSymbol? DeclaringType, string Usage);
