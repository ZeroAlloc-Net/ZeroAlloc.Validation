using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Decides whether the generated validator can name a <c>[Validate]</c> model at all, shared by
/// ValidatorGenerator, which reports ZV0025 and generates nothing for a model it cannot reach,
/// and by the Inject, Options and ASP.NET Core generators, which then leave the model out, so
/// they never refer to a validator that does not exist, issue #216.
/// </summary>
/// <remarks>
/// The validator is a top-level class in the model's namespace, declared in its own generated
/// file. It reaches a model that is accessible anywhere in the assembly: a type nested in
/// another one works as long as it and every type containing it are public, internal or
/// protected internal, issue #207. A <c>private</c>, <c>protected</c> or <c>private protected</c>
/// nested type, or one inside such a type, is out of reach. So is a <c>file</c>-local type, or
/// one nested inside it, because the validator is declared in another file.
/// </remarks>
internal static class GeneratedValidatorReach
{
    /// <summary>Whether the generated validator can refer to <paramref name="model"/>.</summary>
    public static bool CanReach(INamedTypeSymbol model, Compilation compilation)
    {
        for (INamedTypeSymbol? type = model; type is not null; type = type.ContainingType)
        {
            if (type.IsFileLocal)
                return false;
        }

        return compilation.IsSymbolAccessibleWithin(model, compilation.Assembly);
    }

    /// <summary>
    /// The collected models without the unreachable ones, which a companion generator's
    /// transform marks as <see langword="null"/>.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> WithoutUnreachable(ImmutableArray<INamedTypeSymbol?> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
            return ImmutableArray<INamedTypeSymbol>.Empty;

        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (candidate is not null)
                builder.Add(candidate);
        }
        return builder.ToImmutable();
    }
}
