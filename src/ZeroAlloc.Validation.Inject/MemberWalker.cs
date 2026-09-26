using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Enumerates the members a generated validator may act on, including those inherited from
/// base types. <see cref="INamedTypeSymbol.GetMembers()"/> only returns members *declared* on
/// the type, so every discovery pass in this generator goes through here instead.
/// </summary>
/// <remarks>
/// Shared as source with the Inject, Options and ASP.NET Core generators, like the other files
/// in this folder, so the validators they register are exactly the ones a generated constructor
/// takes, issue #246. ValidatorGenerator adds the ZV0017 part, which needs its custom-rule
/// recognition, in its own MemberWalker.InaccessibleBaseMembers.cs.
/// </remarks>
internal static partial class MemberWalker
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

    /// <summary>
    /// Members of <paramref name="type"/> and its base chain, base-most type first and in
    /// declaration order within each type, so inherited rules run before the ones declared on
    /// the derived type. A member hidden by a more-derived declaration (<c>new</c> or
    /// <c>override</c>) is yielded once, from the most-derived type that declares it.
    /// Base members the generated validator could not legally reference are skipped —
    /// ValidatorGenerator's <c>GetInaccessibleBaseMembers</c> reports those separately as ZV0017. A property
    /// the validator cannot read as <c>instance.Prop</c>, on any level, is skipped as well — see
    /// <see cref="GetUnreadableReason"/>; ZV0027 reports a rule placed on one.
    /// Returns only <paramref name="type"/>'s own members when
    /// <c>[Validate(IncludeBaseProperties = false)]</c> is set.
    /// </summary>
    public static ImmutableArray<ISymbol> GetMembersIncludingBase(INamedTypeSymbol type, Compilation compilation)
    {
        if (!IncludesBaseProperties(type))
            return ReadableMembers(type, compilation);

        // Derived -> base, so the most-derived declaration of a hidden member is the one kept.
        var levels = new List<INamedTypeSymbol>();
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            levels.Add(current);

        if (levels.Count <= 1)
            return ReadableMembers(type, compilation);

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
                if (member is IPropertySymbol property && GetUnreadableReason(property, compilation) != UnreadableReason.None)
                {
                    if (IsAccessible(property, compilation))
                        hiding.Add(property);
                    continue;
                }

                if (isBase && !IsAccessibleFrom(member, compilation)) continue;
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
    /// Why the generated validator, an unrelated class in the assembly
    /// <paramref name="compilation"/> builds, cannot read <paramref name="property"/> as
    /// <c>instance.Prop</c>, or <see cref="UnreadableReason.None"/> when it can. The shape of the
    /// property is checked before its accessibility, so a private static property reports as static.
    /// </summary>
    public static UnreadableReason GetUnreadableReason(IPropertySymbol property, Compilation compilation)
    {
        if (property.IsIndexer) return UnreadableReason.Indexer;
        if (property.IsStatic) return UnreadableReason.Static;
        var getter = FindGetter(property);
        if (getter is null) return UnreadableReason.NoGetter;
        if (!IsAccessible(property, compilation))
            return UnreadableReason.Inaccessible;
        if (!IsAccessible(getter, compilation))
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
    /// Whether <paramref name="member"/>, declared on the validated type or one of its base
    /// types, hides a base declaration of the same name from the generated validator,
    /// as <see cref="GetMembersIncludingBase"/> treats it: name lookup from the validator skips a
    /// member it cannot access, but binds to an accessible one even when it cannot be read.
    /// </summary>
    public static bool HidesBaseMembers(ISymbol member, Compilation compilation) =>
        member is IPropertySymbol or IFieldSymbol
        && IsAccessible(member, compilation);

    /// <summary>
    /// Whether <paramref name="reason"/> holds whatever the property's accessibility: a static
    /// property, an indexer, or one with no getter.
    /// </summary>
    public static bool IsUnreadableByShape(UnreadableReason reason) =>
        reason is UnreadableReason.Static or UnreadableReason.Indexer or UnreadableReason.NoGetter;

    /// <summary>
    /// <paramref name="type"/>'s own members without the properties the validator cannot read.
    /// </summary>
    private static ImmutableArray<ISymbol> ReadableMembers(INamedTypeSymbol type, Compilation compilation)
    {
        var members = type.GetMembers();
        var builder = ImmutableArray.CreateBuilder<ISymbol>(members.Length);
        foreach (var member in members)
        {
            if (member is IPropertySymbol property && GetUnreadableReason(property, compilation) != UnreadableReason.None)
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
    /// Whether the generated validator may reference <paramref name="member"/>, and for a
    /// property also call its getter; see <see cref="IsAccessible"/>.
    /// </summary>
    private static bool IsAccessibleFrom(ISymbol member, Compilation compilation)
    {
        if (!IsAccessible(member, compilation))
            return false;

        // A property also needs a getter the validator can call.
        if (member is IPropertySymbol prop)
        {
            var getter = FindGetter(prop);
            if (getter is null) return false;
            if (!IsAccessible(getter, compilation))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the generated validator, a top-level class in the assembly
    /// <paramref name="compilation"/> builds and outside the model's type hierarchy, may
    /// reference <paramref name="member"/>. The compiler decides: <c>protected</c> and
    /// <c>private</c> members are out of reach, and an <c>internal</c> member declared in another
    /// assembly is in reach when that assembly grants this one <c>[InternalsVisibleTo]</c>.
    /// </summary>
    private static bool IsAccessible(ISymbol member, Compilation compilation) =>
        compilation.IsSymbolAccessibleWithin(member, compilation.Assembly);
}
