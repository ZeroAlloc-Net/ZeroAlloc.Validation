using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// The ValidatorGenerator-only part of <see cref="MemberWalker"/>: ZV0017 needs
/// <see cref="ZeroAlloc.Validation.Generator.CustomRules"/>, which the other generators that
/// share MemberWalker do not compile in.
/// </summary>
internal static partial class MemberWalker
{
    /// <summary>
    /// Base-type members that carry ZeroAlloc validation attributes but cannot be referenced
    /// from the generated validator (a separate class), so their rules are silently dropped.
    /// Reported as ZV0017 rather than emitting code that would not compile. A member that the
    /// generation of a <c>[Validate]</c> base type reports instead is still returned; the caller
    /// filters it with <see cref="MethodReachability.IsReportedByBaseValidator"/>, which knows
    /// whether that base type walks the member's declaring type.
    /// </summary>
    public static IEnumerable<ISymbol> GetInaccessibleBaseMembers(INamedTypeSymbol type, Compilation compilation)
    {
        if (!IncludesBaseProperties(type)) yield break;

        for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol && member is not IMethodSymbol) continue;
                // A static, getter-less or indexer property cannot be read however accessible it
                // is; that is ZV0027's case, reported at each rule, not an accessibility problem.
                if (member is IPropertySymbol property && IsUnreadableByShape(GetUnreadableReason(property, compilation))) continue;
                if (IsAccessibleFrom(member, compilation)) continue;
                if (!HasZeroAllocValidationAttribute(member)) continue;
                yield return member;
            }
        }
    }

    private static bool HasZeroAllocValidationAttribute(ISymbol member)
    {
        foreach (var attr in member.GetAttributes())
        {
            var ns = attr.AttributeClass?.ContainingNamespace?.ToDisplayString();
            if (string.Equals(ns, "ZeroAlloc.Validation", StringComparison.Ordinal)
                || ZeroAlloc.Validation.Generator.CustomRules.IsCustomRule(attr))
                return true;
        }
        return false;
    }
}
