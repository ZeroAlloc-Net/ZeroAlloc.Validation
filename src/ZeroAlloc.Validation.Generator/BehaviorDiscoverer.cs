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
            var cleanName = info.BehaviorTypeName
                .Replace("global::", string.Empty)
                .Replace("+", ".");   // handle nested types

            var symbol = compilation.GetTypeByMetadataName(cleanName);
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
