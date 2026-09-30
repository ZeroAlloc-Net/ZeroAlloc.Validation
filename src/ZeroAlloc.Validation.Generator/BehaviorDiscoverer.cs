using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Pipeline.Generators;

namespace ZeroAlloc.Validation.Generator;

internal static class BehaviorDiscoverer
{
    /// <summary>
    /// Discovers all [PipelineBehavior] classes in the compilation and classifies them as
    /// sync (Handle returns ValidationResult) or async (Handle returns ValueTask&lt;ValidationResult&gt;).
    /// </summary>
    public static (List<PipelineBehaviorInfo> Sync, List<PipelineBehaviorInfo> Async)
        DiscoverAll(Compilation compilation)
    {
        var sync  = new List<PipelineBehaviorInfo>();
        var async_ = new List<PipelineBehaviorInfo>();

        foreach (var info in PipelineBehaviorDiscoverer.Discover(compilation))
        {
            // Re-resolve the symbol to inspect the Handle method return type.
            var symbol = ResolveSymbol(compilation, info.BehaviorTypeName);
            if (symbol is null) continue;

            if (IsAsyncBehavior(symbol))
                async_.Add(info);
            else
                sync.Add(info);
        }

        return (sync, async_);
    }

    /// <summary>
    /// Filters the full behavior lists down to those applicable for a specific model FQN,
    /// sorted by Order ascending. A null AppliesTo means global (applies to all models).
    /// A generic model also matches its open form, <paramref name="unboundName"/>, which is how
    /// <c>typeof(Page&lt;&gt;)</c> is written; see <see cref="UnboundName"/>, issue #238.
    /// </summary>
    public static (List<PipelineBehaviorInfo> Sync, List<PipelineBehaviorInfo> Async) ForModel(
        IReadOnlyList<PipelineBehaviorInfo> allSync,
        IReadOnlyList<PipelineBehaviorInfo> allAsync,
        string modelFqn,
        string? unboundName)
    {
        bool Applies(PipelineBehaviorInfo b) =>
            b.AppliesTo is null
            || string.Equals(b.AppliesTo, modelFqn, System.StringComparison.Ordinal)
            || (unboundName is not null && string.Equals(b.AppliesTo, unboundName, System.StringComparison.Ordinal));

        var sync  = allSync .Where(Applies).OrderBy(b => b.Order).ToList();
        var async_ = allAsync.Where(Applies).OrderBy(b => b.Order).ToList();

        return (sync, async_);
    }

    /// <summary>
    /// The open form of a generic model as <c>typeof</c> names it, with <c>&lt;&gt;</c>, or
    /// <c>&lt;,&gt;</c> and so on, for each generic type along the containing chain:
    /// <c>global::Ns.Page&lt;&gt;</c>, <c>global::Ns.Outer&lt;&gt;.Inner&lt;&gt;</c> or
    /// <c>global::Ns.Outer&lt;&gt;.Plain</c>, or without the namespace when
    /// <paramref name="qualified"/> is false. <see langword="null"/> for a model that is not
    /// generic. The chain is walked rather than asking Roslyn for the unbound type, which it
    /// cannot build for <c>Plain</c>, whose own arity is zero.
    /// </summary>
    public static string? UnboundName(INamedTypeSymbol model, bool qualified = true)
    {
        if (!Shared.GeneratedValidatorReach.IsGeneric(model))
            return null;

        var sb = new System.Text.StringBuilder();
        AppendUnbound(sb, model.OriginalDefinition, qualified);
        return sb.ToString();
    }

    private static void AppendUnbound(System.Text.StringBuilder sb, INamedTypeSymbol type, bool qualified)
    {
        if (type.ContainingType is { } container)
        {
            AppendUnbound(sb, container, qualified);
            sb.Append('.');
        }
        else if (qualified)
        {
            // Written as FullyQualifiedFormat writes the model's name, keyword segments escaped.
            if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
                sb.Append(ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append('.');
            else
                sb.Append("global::");
        }

        sb.Append(GeneratedCalls.Identifier(type.Name));
        if (type.Arity > 0)
            sb.Append('<').Append(',', type.Arity - 1).Append('>');
    }

    /// <summary>
    /// The type the <c>[PipelineBehavior]</c> attribute on <paramref name="behavior"/> names with
    /// <c>AppliesTo</c>, or <see langword="null"/> when it names none.
    /// </summary>
    public static INamedTypeSymbol? AppliesToType(INamedTypeSymbol behavior)
    {
        foreach (var attr in behavior.GetAttributes())
        {
            for (var attrClass = attr.AttributeClass; attrClass is not null; attrClass = attrClass.BaseType)
            {
                if (!string.Equals(attrClass.ToDisplayString(), "ZeroAlloc.Pipeline.PipelineBehaviorAttribute", System.StringComparison.Ordinal))
                    continue;
                foreach (var named in attr.NamedArguments)
                {
                    if (string.Equals(named.Key, "AppliesTo", System.StringComparison.Ordinal))
                        return named.Value.Value as INamedTypeSymbol;
                }
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is written in its open form, <c>typeof(Page&lt;&gt;)</c> or
    /// <c>typeof(Outer&lt;&gt;.Plain)</c>: a type along its containing chain is unbound.
    /// </summary>
    public static bool IsOpenForm(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsUnboundGenericType)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The <c>[PipelineBehavior]</c> types that do not implement <c>IPipelineBehavior</c>.
    /// <see cref="DiscoverAll"/> leaves them out of every pipeline, so they never run; ZV0035
    /// reports them, issue #288.
    /// </summary>
    public static IEnumerable<PipelineBehaviorCandidateInfo> DiscoverMissingInterface(Compilation compilation) =>
        PipelineDiagnosticRules.FindMissingPipelineBehaviorInterface(
            PipelineBehaviorDiscoverer.DiscoverCandidates(compilation));

    /// <summary>
    /// Re-resolves a <see cref="PipelineBehaviorInfo.BehaviorTypeName"/> (e.g.
    /// <c>"global::App.Outer.Inner"</c>) back to its symbol in <paramref name="compilation"/>, or
    /// null when it cannot be found — e.g. the type came from a stale cache entry. Shared by
    /// <see cref="DiscoverAll"/> and the location lookup of ZV0015 and ZV0035 in
    /// <c>ValidatorGenerator</c>.
    /// </summary>
    /// <remarks>
    /// The name is in <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>, which separates a
    /// nested type from its containing type with a dot, where its metadata name has a plus:
    /// <c>App.Outer+Inner</c>. So each dot, from the right, is tried as a nesting separator.
    /// </remarks>
    internal static INamedTypeSymbol? ResolveSymbol(Compilation compilation, string behaviorTypeName)
    {
        const string GlobalPrefix = "global::";
        var name = behaviorTypeName.StartsWith(GlobalPrefix, System.StringComparison.Ordinal)
            ? behaviorTypeName.Substring(GlobalPrefix.Length)
            : behaviorTypeName;

        var metadataName = name.ToCharArray();
        var dot = metadataName.Length;
        while (true)
        {
            var symbol = compilation.GetTypeByMetadataName(new string(metadataName));
            if (symbol is not null || dot == 0) return symbol;

            dot = System.Array.LastIndexOf(metadataName, '.', dot - 1);
            if (dot < 0) return null;
            metadataName[dot] = '+';
        }
    }

    private static bool IsAsyncBehavior(INamedTypeSymbol symbol)
    {
        var current = symbol;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IMethodSymbol method) continue;
                if (!string.Equals(method.Name, "Handle", System.StringComparison.Ordinal)) continue;
                if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Public) continue;
                if (method.TypeParameters.Length == 0) continue;

                // ValueTask<T> return type — original definition is "System.Threading.Tasks.ValueTask<TResult>"
                if (method.ReturnType is INamedTypeSymbol rt
                    && rt.IsGenericType
                    && string.Equals(
                        rt.OriginalDefinition.ToDisplayString(),
                        "System.Threading.Tasks.ValueTask<TResult>",
                        System.StringComparison.Ordinal))
                    return true;
            }
            current = current.BaseType;
        }
        return false;
    }
}
