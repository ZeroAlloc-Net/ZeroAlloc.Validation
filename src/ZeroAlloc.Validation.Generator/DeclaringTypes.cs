using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// The types whose usages the generated validators read, as the declaring-type step sees them.
/// A usage on a type is reported by that type's step rather than by each model walking it, so a
/// plain base type shared by several <c>[Validate]</c> models reports it once, and an edit to one
/// of the models leaves the base type's diagnostics cached, issue #290.
/// </summary>
internal static class DeclaringTypes
{
    // Nullable annotations are part of the construction: a rule on a T property converts from
    // string? and string differently, ZV0021.
    private static readonly SymbolDisplayFormat ConstructionFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// The key a declaring type's step goes by: its definition, so every construction of a generic
    /// type reports through one step, and a diagnostic that does not depend on the type arguments
    /// is reported once.
    /// </summary>
    public static string Key(INamedTypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// The types <paramref name="model"/>'s validator walks, itself and each base type whose
    /// properties it includes, with the diagnostics <paramref name="diagnostics"/> holds for them.
    /// The walk follows the one the validator makes: the rules come from
    /// <see cref="MemberWalker.GetMembersIncludingBase"/>, and a base type from a referenced
    /// assembly is kept only when it has a property whose rules the validator reads, since ZV0024
    /// and ZV0027 look at types declared in source only.
    /// </summary>
    public static EquatableArray<TypeWalk> Walk(INamedTypeSymbol model, Compilation compilation, DiagnosticSink diagnostics)
    {
        var ruleProperties = new Dictionary<INamedTypeSymbol, List<string>>(SymbolEqualityComparer.Default);
        foreach (var member in MemberWalker.GetMembersIncludingBase(model, compilation))
        {
            if (member is not IPropertySymbol property) continue;
            if (!ruleProperties.TryGetValue(property.ContainingType, out var names))
                ruleProperties[property.ContainingType] = names = [];
            names.Add(property.Name);
        }

        var resolver = MetadataName(model);
        var includeBase = MemberWalker.IncludesBaseProperties(model);
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        var walks = new List<TypeWalk>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        for (var type = model; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            bool isBase = !SymbolEqualityComparer.Default.Equals(type, model);
            if (isBase && !includeBase)
                break;

            bool inSource = type.Locations.Any(l => l.IsInSource);
            ruleProperties.TryGetValue(type, out var rules);
            if (inSource || rules is not null)
            {
                var checkedProperties = new List<string>();
                if (inSource)
                {
                    foreach (var member in type.GetMembers())
                    {
                        if (member is IPropertySymbol property && !hidden.Contains(property.Name))
                            checkedProperties.Add(property.Name);
                    }
                }
                var construction = new TypeConstruction(
                    type.ToDisplayString(ConstructionFormat),
                    resolver,
                    AsModel: !isBase,
                    Sorted(rules ?? []),
                    Sorted(checkedProperties));
                var key = Key(type);
                if (keys.Add(key))
                    walks.Add(new TypeWalk(key, construction, diagnostics.SharedWith(key)));
            }

            foreach (var member in type.GetMembers())
            {
                if (MemberWalker.HidesBaseMembers(member, compilation))
                    hidden.Add(member.Name);
            }
        }

        foreach (var key in diagnostics.SharedTypeKeys())
        {
            if (keys.Add(key))
                walks.Add(new TypeWalk(key, null, diagnostics.SharedWith(key)));
        }

        return EquatableArray.From(walks);
    }

    /// <summary>
    /// Every model's walks merged per declaring type, in the order the types are first walked.
    /// The walks of one construction are merged into one, reading every property any model reads,
    /// and the shared diagnostics are kept once each.
    /// </summary>
    public static ImmutableArray<DeclaringTypeUsages> Merge(ImmutableArray<TypeWalk> walks)
    {
        var order = new List<string>();
        var constructions = new Dictionary<string, List<TypeConstruction>>(StringComparer.Ordinal);
        var shared = new Dictionary<string, List<DiagnosticInfo>>(StringComparer.Ordinal);

        foreach (var walk in walks)
        {
            if (!constructions.TryGetValue(walk.Key, out var forType))
            {
                order.Add(walk.Key);
                constructions[walk.Key] = forType = [];
                shared[walk.Key] = [];
            }

            if (walk.Construction is { } construction)
            {
                var index = forType.FindIndex(c => string.Equals(c.Construction, construction.Construction, StringComparison.Ordinal));
                if (index < 0)
                    forType.Add(construction);
                else
                    forType[index] = Union(forType[index], construction);
            }

            var diagnostics = shared[walk.Key];
            foreach (var diagnostic in walk.Shared)
            {
                if (!diagnostics.Contains(diagnostic))
                    diagnostics.Add(diagnostic);
            }
        }

        var builder = ImmutableArray.CreateBuilder<DeclaringTypeUsages>(order.Count);
        for (var i = 0; i < order.Count; i++)
            builder.Add(new DeclaringTypeUsages(order[i], EquatableArray.From(constructions[order[i]]), EquatableArray.From(shared[order[i]])));
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// The type <paramref name="construction"/> names in <paramref name="compilation"/>: its
    /// resolver model, or the base type of it with that construction. <see langword="null"/> when
    /// the model is gone, which a later run of the pipeline catches up with.
    /// </summary>
    public static INamedTypeSymbol? Resolve(TypeConstruction construction, Compilation compilation)
    {
        for (var type = compilation.Assembly.GetTypeByMetadataName(construction.ResolverModel); type is not null; type = type.BaseType)
        {
            if (string.Equals(type.ToDisplayString(ConstructionFormat), construction.Construction, StringComparison.Ordinal))
                return type;
        }
        return null;
    }

    /// <summary>
    /// The name <see cref="IAssemblySymbol.GetTypeByMetadataName"/> finds <paramref name="type"/>
    /// by: its namespace, its containing types joined by <c>+</c>, and its own metadata name.
    /// </summary>
    private static string MetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } containing)
            return MetadataName(containing) + "+" + type.MetadataName;
        return type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString() + "." + type.MetadataName
            : type.MetadataName;
    }

    private static TypeConstruction Union(TypeConstruction left, TypeConstruction right) =>
        left with
        {
            AsModel = left.AsModel || right.AsModel,
            RuleProperties = Sorted(left.RuleProperties.Concat(right.RuleProperties)),
            CheckedProperties = Sorted(left.CheckedProperties.Concat(right.CheckedProperties)),
        };

    private static EquatableArray<string> Sorted(IEnumerable<string> names) =>
        EquatableArray.From(names.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList());
}
