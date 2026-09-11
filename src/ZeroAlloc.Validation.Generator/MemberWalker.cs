using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Enumerates the members a generated validator may act on, including those inherited from
/// base types. <see cref="INamedTypeSymbol.GetMembers()"/> only returns members *declared* on
/// the type, so every discovery pass in this generator goes through here instead.
/// </summary>
internal static class MemberWalker
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

    /// <summary>
    /// Members of <paramref name="type"/> and its base chain, base-most type first and in
    /// declaration order within each type, so inherited rules run before the ones declared on
    /// the derived type. A member hidden by a more-derived declaration (<c>new</c> or
    /// <c>override</c>) is yielded once, from the most-derived type that declares it.
    /// Base members the generated validator could not legally reference are skipped —
    /// <see cref="GetInaccessibleBaseMembers"/> reports those separately as ZV0017.
    /// Returns only <paramref name="type"/>'s own members when
    /// <c>[Validate(IncludeBaseProperties = false)]</c> is set.
    /// </summary>
    public static ImmutableArray<ISymbol> GetMembersIncludingBase(INamedTypeSymbol type)
    {
        if (!IncludesBaseProperties(type))
            return type.GetMembers();

        // Derived -> base, so the most-derived declaration of a hidden member is the one kept.
        var levels = new List<INamedTypeSymbol>();
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            levels.Add(current);

        if (levels.Count <= 1)
            return type.GetMembers();

        var perLevel = new List<List<ISymbol>>(levels.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < levels.Count; i++)
        {
            bool isBase = i > 0;
            var kept = new List<ISymbol>();
            foreach (var member in levels[i].GetMembers())
            {
                // A more-derived level already contributed this member — that declaration wins,
                // whether it hides the base one with `new` or overrides it.
                if (seen.Contains(HidingKey(member))) continue;
                if (isBase && !IsAccessibleFrom(member, type)) continue;
                kept.Add(member);
            }

            for (int k = 0; k < kept.Count; k++)
                seen.Add(HidingKey(kept[k]));

            perLevel.Add(kept);
        }

        // Emit base-most first.
        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        for (int i = perLevel.Count - 1; i >= 0; i--)
        {
            var level = perLevel[i];
            for (int k = 0; k < level.Count; k++)
                builder.Add(level[k]);
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Base-type members that carry ZeroAlloc validation attributes but cannot be referenced
    /// from the generated validator (a separate class), so their rules are silently dropped.
    /// Reported as ZV0017 rather than emitting code that would not compile.
    /// </summary>
    public static IEnumerable<ISymbol> GetInaccessibleBaseMembers(INamedTypeSymbol type)
    {
        if (!IncludesBaseProperties(type)) yield break;

        for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol && member is not IMethodSymbol) continue;
                if (IsAccessibleFrom(member, type)) continue;
                if (!HasZeroAllocValidationAttribute(member)) continue;
                yield return member;
            }
        }
    }

    /// <summary>
    /// Identity a more-derived declaration hides a base one by: properties collide on name,
    /// methods only when the whole signature matches, so an unrelated overload in the derived
    /// type cannot silently swallow a base <c>[CustomValidation]</c> method.
    /// </summary>
    private static string HidingKey(ISymbol member)
    {
        if (member is not IMethodSymbol method)
            return "P:" + member.Name;

        var sb = new System.Text.StringBuilder("M:").Append(method.Name).Append('(');
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(method.Parameters[i].Type.ToDisplayString());
        }
        return sb.Append(')').ToString();
    }

    /// <summary>
    /// Whether a <c>When</c>/<c>Unless</c> method named <paramref name="methodName"/> can be
    /// called from the generated validator for <paramref name="type"/>. A method declared on
    /// <paramref name="type"/> itself — or one that does not resolve at all — is left to the
    /// compiler to judge, unchanged; only a method reached through the base chain is screened,
    /// so an inherited rule guarded by a <c>protected</c> helper is dropped with ZV0017 instead
    /// of emitting a call that cannot compile.
    /// </summary>
    public static bool IsConditionMethodAccessible(INamedTypeSymbol type, string methodName)
    {
        bool isBase = false;
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(methodName))
            {
                if (member is not IMethodSymbol method) continue;
                if (method.Parameters.Length != 0) continue;
                return !isBase || IsAccessibleFrom(method, type);
            }
            isBase = true;
        }
        return true;
    }

    /// <summary>
    /// Whether <c>[Validate]</c> on <paramref name="type"/> leaves base-type rules switched on.
    /// Absent the named argument the answer is <see langword="true"/> — inheriting base rules is
    /// the default, matching the attribute's own <c>IncludeBaseProperties = true</c> initializer.
    /// </summary>
    public static bool IncludesBaseProperties(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (!string.Equals(attr.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, StringComparison.Ordinal))
                continue;
            foreach (var named in attr.NamedArguments)
            {
                if (string.Equals(named.Key, "IncludeBaseProperties", StringComparison.Ordinal)
                    && named.Value.Value is bool b)
                    return b;
            }
            return true;
        }
        return true;
    }

    /// <summary>
    /// Whether the generated validator for <paramref name="accessingType"/> — an unrelated class
    /// in the same assembly — may reference <paramref name="member"/>. <c>protected</c> and
    /// <c>private</c> members are not reachable from it, and <c>internal</c> members only when
    /// the declaring assembly is the one being compiled.
    /// </summary>
    private static bool IsAccessibleFrom(ISymbol member, INamedTypeSymbol accessingType)
    {
        if (!IsAccessibleAccessibility(member.DeclaredAccessibility, member, accessingType))
            return false;

        // A property also needs a getter the validator can call.
        if (member is IPropertySymbol prop)
        {
            if (prop.GetMethod is null) return false;
            if (!IsAccessibleAccessibility(prop.GetMethod.DeclaredAccessibility, prop.GetMethod, accessingType))
                return false;
        }

        return true;
    }

    private static bool IsAccessibleAccessibility(Accessibility accessibility, ISymbol member, INamedTypeSymbol accessingType) =>
        accessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Internal or Accessibility.ProtectedOrInternal =>
                SymbolEqualityComparer.Default.Equals(
                    member.ContainingAssembly, accessingType.ContainingAssembly),
            _ => false,
        };

    private static bool HasZeroAllocValidationAttribute(ISymbol member)
    {
        foreach (var attr in member.GetAttributes())
        {
            var ns = attr.AttributeClass?.ContainingNamespace?.ToDisplayString();
            if (string.Equals(ns, "ZeroAlloc.Validation", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
