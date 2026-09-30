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
    /// </summary>
    public static (List<PipelineBehaviorInfo> Sync, List<PipelineBehaviorInfo> Async) ForModel(
        IReadOnlyList<PipelineBehaviorInfo> allSync,
        IReadOnlyList<PipelineBehaviorInfo> allAsync,
        string modelFqn)
    {
        static bool Applies(PipelineBehaviorInfo b, string fqn) =>
            b.AppliesTo is null ||
            string.Equals(b.AppliesTo, fqn, System.StringComparison.Ordinal);

        var sync  = allSync .Where(b => Applies(b, modelFqn)).OrderBy(b => b.Order).ToList();
        var async_ = allAsync.Where(b => Applies(b, modelFqn)).OrderBy(b => b.Order).ToList();

        return (sync, async_);
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
