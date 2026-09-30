using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Finds the properties of a generic <c>[Validate]</c> model whose nested validation would nest
/// the model inside itself without end, ZV0037, issue #238. Such a property is not composed:
/// <see cref="ValidatorDependencies.Of"/> leaves it out, so the generated constructor does not
/// take a validator for it and no walk over the validators a constructor takes, such as the
/// registration graph or the asynchronous-rules check, runs forever.
/// </summary>
/// <remarks>
/// <para>
/// <c>class Node&lt;T&gt; { public Node&lt;Node&lt;T&gt;&gt;? Next { get; set; } }</c> compiles. Its
/// validator takes <c>ValidatorFor&lt;Node&lt;Node&lt;T&gt;&gt;&gt;</c>, whose validator takes
/// <c>ValidatorFor&lt;Node&lt;Node&lt;Node&lt;T&gt;&gt;&gt;&gt;</c>, and so on: every closing is new,
/// so a walk that stops at a closing it has seen never stops. The chain can also run through
/// other models: <c>A&lt;T&gt;</c> holding a <c>B&lt;List&lt;T&gt;&gt;</c> and <c>B&lt;U&gt;</c>
/// holding an <c>A&lt;U&gt;</c> grow the type argument by one <c>List</c> on every round.
/// </para>
/// <para>
/// The check is the one the runtime makes for an expanding generic cycle, over the models'
/// composition. Each composed property of a generic model links each type parameter of the model
/// that its type argument mentions to the corresponding type parameter of the model it is
/// validated as. The link grows when the argument is not the type parameter itself but contains
/// it, as <c>Node&lt;T&gt;</c> contains <c>T</c>. The walks are unbounded exactly when a cycle of
/// links contains a growing one, so a property is expanding when one of its growing links lies on
/// such a cycle. Leaving those properties out removes every growing cycle, and the walks, which
/// never visit a closing twice, then end. Plain self-reference, <c>Node&lt;T&gt;? Next</c>, links
/// <c>T</c> to itself without growing and is composed as for a non-generic model; so is a property
/// that reorders or replaces the type arguments, such as <c>Pair&lt;U, T&gt;</c> in
/// <c>Pair&lt;T, U&gt;</c> or <c>Node&lt;string&gt;</c>, whose closings are finite.
/// </para>
/// <para>
/// A model that is not generic has no type parameters to link, so none of its properties is ever
/// expanding: its closed dependencies are walked, and any expanding property of theirs is left
/// out by their own model's check.
/// </para>
/// </remarks>
internal static class ExpandingComposition
{
    /// <summary>
    /// The answers per compilation, by model definition. A compilation is immutable, so an answer
    /// never goes stale, and the table holds it weakly.
    /// </summary>
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<INamedTypeSymbol, List<IPropertySymbol>>> Cache = new();

    /// <summary>
    /// Whether <paramref name="property"/>, walked from <paramref name="model"/> or a closing of it,
    /// is one of <see cref="Properties"/>.
    /// </summary>
    public static bool IsExpanding(INamedTypeSymbol model, IPropertySymbol property, Compilation compilation)
    {
        if (!GeneratedValidatorReach.IsGeneric(model))
            return false;

        var expanding = Properties(model, compilation);
        for (var i = 0; i < expanding.Count; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(expanding[i].OriginalDefinition, property.OriginalDefinition))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The properties of <paramref name="model"/>'s definition, as its own walk yields them, whose
    /// nested validation would nest a model inside itself without end. Empty for a model that is
    /// not generic.
    /// </summary>
    public static List<IPropertySymbol> Properties(INamedTypeSymbol model, Compilation compilation)
    {
        var definition = model.OriginalDefinition;
        if (!GeneratedValidatorReach.IsGeneric(definition))
            return [];

        var cache = Cache.GetValue(compilation, static _ => new ConcurrentDictionary<INamedTypeSymbol, List<IPropertySymbol>>(SymbolEqualityComparer.Default));
        return cache.GetOrAdd(definition, d => Find(d, compilation));
    }

    private static List<IPropertySymbol> Find(INamedTypeSymbol definition, Compilation compilation)
    {
        var graph = new Dictionary<INamedTypeSymbol, List<Link>>(SymbolEqualityComparer.Default);
        var result = new List<IPropertySymbol>();
        var links = LinksOf(definition, compilation, graph);
        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (!link.Grows || result.Contains(link.Property))
                continue;
            if (Reaches(link.To, new Node(definition, link.From), compilation, graph))
                result.Add(link.Property);
        }
        return result;
    }

    /// <summary>A type parameter of a model definition, by its position among the validator's type parameters.</summary>
    private readonly struct Node : IEquatable<Node>
    {
        public Node(INamedTypeSymbol model, int index)
        {
            Model = model;
            Index = index;
        }

        public INamedTypeSymbol Model { get; }

        public int Index { get; }

        public bool Equals(Node other) =>
            Index == other.Index && SymbolEqualityComparer.Default.Equals(Model, other.Model);

        public override bool Equals(object? obj) => obj is Node other && Equals(other);

        public override int GetHashCode() =>
            unchecked((SymbolEqualityComparer.Default.GetHashCode(Model) * 31) + Index);
    }

    /// <summary>
    /// A link from type parameter <see cref="From"/> of a model to type parameter <see cref="To"/>
    /// of the model <see cref="Property"/> is validated as, which <see cref="Grows"/> when the type
    /// argument contains the type parameter rather than being it.
    /// </summary>
    private sealed class Link
    {
        public Link(IPropertySymbol property, int from, Node to, bool grows)
        {
            Property = property;
            From = from;
            To = to;
            Grows = grows;
        }

        public IPropertySymbol Property { get; }

        public int From { get; }

        public Node To { get; }

        public bool Grows { get; }
    }

    private static bool Reaches(Node start, Node target, Compilation compilation, Dictionary<INamedTypeSymbol, List<Link>> graph)
    {
        var seen = new HashSet<Node> { start };
        var pending = new Queue<Node>();
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            if (node.Equals(target))
                return true;

            var links = LinksOf(node.Model, compilation, graph);
            for (var i = 0; i < links.Count; i++)
            {
                if (links[i].From == node.Index && seen.Add(links[i].To))
                    pending.Enqueue(links[i].To);
            }
        }
        return false;
    }

    /// <summary>
    /// The links out of <paramref name="definition"/>'s type parameters, one per composed property
    /// and type parameter its type argument mentions, computed once per walk.
    /// </summary>
    private static List<Link> LinksOf(INamedTypeSymbol definition, Compilation compilation, Dictionary<INamedTypeSymbol, List<Link>> graph)
    {
        if (graph.TryGetValue(definition, out var known))
            return known;

        var links = new List<Link>();
        graph[definition] = links;

        var parameters = GeneratedValidatorNames.TypeArguments(definition);
        if (parameters.Count == 0)
            return links;

        foreach (var (property, composed) in ValidatorDependencies.AutoComposedCandidates(definition, compilation))
        {
            var target = composed.OriginalDefinition;
            var arguments = GeneratedValidatorNames.TypeArguments(composed);
            for (var j = 0; j < arguments.Count; j++)
            {
                var mentioned = new List<ITypeParameterSymbol>();
                CollectTypeParameters(arguments[j], mentioned);
                for (var k = 0; k < mentioned.Count; k++)
                {
                    var parameter = mentioned[k];
                    var from = IndexOf(parameters, parameter);
                    if (from < 0)
                        continue;
                    var grows = !SymbolEqualityComparer.Default.Equals(arguments[j], parameter);
                    links.Add(new Link(property, from, new Node(target, j), grows));
                }
            }
        }
        return links;
    }

    private static int IndexOf(List<ITypeSymbol> parameters, ITypeParameterSymbol parameter)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(parameters[i], parameter))
                return i;
        }
        return -1;
    }

    private static void CollectTypeParameters(ITypeSymbol type, List<ITypeParameterSymbol> found)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                if (!found.Contains(parameter))
                    found.Add(parameter);
                break;
            case IArrayTypeSymbol array:
                CollectTypeParameters(array.ElementType, found);
                break;
            case IPointerTypeSymbol pointer:
                CollectTypeParameters(pointer.PointedAtType, found);
                break;
            case INamedTypeSymbol named:
                if (named.ContainingType is { } container)
                    CollectTypeParameters(container, found);
                foreach (var argument in named.TypeArguments)
                    CollectTypeParameters(argument, found);
                break;
        }
    }
}
