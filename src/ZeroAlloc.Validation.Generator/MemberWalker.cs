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
    /// <see cref="GetInaccessibleBaseMembers"/> reports those separately as ZV0017. A property
    /// the validator cannot read as <c>instance.Prop</c>, on any level, is skipped as well — see
    /// <see cref="GetUnreadableReason"/>; ZV0027 reports a rule placed on one.
    /// Returns only <paramref name="type"/>'s own members when
    /// <c>[Validate(IncludeBaseProperties = false)]</c> is set.
    /// </summary>
    public static ImmutableArray<ISymbol> GetMembersIncludingBase(INamedTypeSymbol type)
    {
        if (!IncludesBaseProperties(type))
            return ReadableMembers(type, type);

        // Derived -> base, so the most-derived declaration of a hidden member is the one kept.
        var levels = new List<INamedTypeSymbol>();
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            levels.Add(current);

        if (levels.Count <= 1)
            return ReadableMembers(type, type);

        var perLevel = new List<List<ISymbol>>(levels.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < levels.Count; i++)
        {
            bool isBase = i > 0;
            var kept = new List<ISymbol>();
            var hiding = new List<ISymbol>();
            foreach (var member in levels[i].GetMembers())
            {
                // A more-derived level already contributed this member — that declaration wins,
                // whether it hides the base one with `new` or overrides it.
                if (seen.Contains(HidingKey(member))) continue;

                // `instance.Prop` binds to a property the validator can see even when it cannot
                // read it, a static one or one without a getter, so that property still hides
                // the base declarations of the same name.
                if (member is IPropertySymbol property && GetUnreadableReason(property, type) != UnreadableReason.None)
                {
                    if (IsAccessibleAccessibility(property.DeclaredAccessibility, property, type))
                        hiding.Add(property);
                    continue;
                }

                if (isBase && !IsAccessibleFrom(member, type)) continue;
                kept.Add(member);
            }

            for (int k = 0; k < kept.Count; k++)
                seen.Add(HidingKey(kept[k]));
            for (int k = 0; k < hiding.Count; k++)
                seen.Add(HidingKey(hiding[k]));

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
    /// Reported as ZV0017 rather than emitting code that would not compile. A member that the
    /// generation of a <c>[Validate]</c> base type reports instead is still returned; the caller
    /// filters it with <see cref="MethodReachability.IsReportedByBaseValidator"/>, which knows
    /// whether that base type walks the member's declaring type.
    /// </summary>
    public static IEnumerable<ISymbol> GetInaccessibleBaseMembers(INamedTypeSymbol type)
    {
        if (!IncludesBaseProperties(type)) yield break;

        for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol && member is not IMethodSymbol) continue;
                // A static, getter-less or indexer property cannot be read however accessible it
                // is; that is ZV0027's case, reported at each rule, not an accessibility problem.
                if (member is IPropertySymbol property && IsUnreadableByShape(GetUnreadableReason(property, type))) continue;
                if (IsAccessibleFrom(member, type)) continue;
                if (!HasZeroAllocValidationAttribute(member)) continue;
                yield return member;
            }
        }
    }

    /// <summary>
    /// Why the generated validator for <paramref name="validatedType"/>, an unrelated class in the
    /// same assembly, cannot read <paramref name="property"/> as <c>instance.Prop</c>, or
    /// <see cref="UnreadableReason.None"/> when it can. The shape of the property is checked
    /// before its accessibility, so a private static property reports as static.
    /// </summary>
    public static UnreadableReason GetUnreadableReason(IPropertySymbol property, INamedTypeSymbol validatedType)
    {
        if (property.IsIndexer) return UnreadableReason.Indexer;
        if (property.IsStatic) return UnreadableReason.Static;
        var getter = FindGetter(property);
        if (getter is null) return UnreadableReason.NoGetter;
        if (!IsAccessibleAccessibility(property.DeclaredAccessibility, property, validatedType))
            return UnreadableReason.Inaccessible;
        if (!IsAccessibleAccessibility(getter.DeclaredAccessibility, getter, validatedType))
            return UnreadableReason.GetterInaccessible;
        return UnreadableReason.None;
    }

    /// <summary>
    /// The getter <c>instance.Prop</c> calls. An override may declare only a setter and inherit
    /// the getter, so the overridden chain is searched until one declares it.
    /// </summary>
    private static IMethodSymbol? FindGetter(IPropertySymbol property)
    {
        for (var current = property; current is not null; current = current.OverriddenProperty)
        {
            if (current.GetMethod is not null)
                return current.GetMethod;
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="member"/>, declared on <paramref name="validatedType"/> or one of
    /// its base types, hides a base declaration of the same name from the generated validator,
    /// as <see cref="GetMembersIncludingBase"/> treats it: name lookup from the validator skips a
    /// member it cannot access, but binds to an accessible one even when it cannot be read.
    /// </summary>
    public static bool HidesBaseMembers(ISymbol member, INamedTypeSymbol validatedType) =>
        member is IPropertySymbol or IFieldSymbol
        && IsAccessibleAccessibility(member.DeclaredAccessibility, member, validatedType);

    /// <summary>
    /// Whether <paramref name="reason"/> holds whatever the property's accessibility: a static
    /// property, an indexer, or one with no getter.
    /// </summary>
    public static bool IsUnreadableByShape(UnreadableReason reason) =>
        reason is UnreadableReason.Static or UnreadableReason.Indexer or UnreadableReason.NoGetter;

    /// <summary>
    /// <paramref name="type"/>'s own members without the properties the validator for
    /// <paramref name="validatedType"/> cannot read.
    /// </summary>
    private static ImmutableArray<ISymbol> ReadableMembers(INamedTypeSymbol type, INamedTypeSymbol validatedType)
    {
        var members = type.GetMembers();
        var builder = ImmutableArray.CreateBuilder<ISymbol>(members.Length);
        foreach (var member in members)
        {
            if (member is IPropertySymbol property && GetUnreadableReason(property, validatedType) != UnreadableReason.None)
                continue;
            builder.Add(member);
        }
        return builder.ToImmutable();
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
            var getter = FindGetter(prop);
            if (getter is null) return false;
            if (!IsAccessibleAccessibility(getter.DeclaredAccessibility, getter, accessingType))
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
            if (string.Equals(ns, "ZeroAlloc.Validation", StringComparison.Ordinal)
                || CustomRules.IsCustomRule(attr))
                return true;
        }
        return false;
    }
}
