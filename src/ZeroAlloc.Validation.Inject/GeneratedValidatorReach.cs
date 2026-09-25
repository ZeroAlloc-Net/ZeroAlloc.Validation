using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Decides whether a <c>[Validate]</c> model gets a generated validator at all, shared by
/// ValidatorGenerator, which reports ZV0025 for a model it cannot reach and ZV0029 for a generic
/// one and generates nothing for either, and by the Inject, Options and ASP.NET Core generators
/// and the nested-validator composition, which then leave the model out, so they never refer to
/// a validator that does not exist, issues #216 and #219.
/// </summary>
/// <remarks>
/// The validator is a top-level class in the model's namespace, declared in its own generated
/// file. It reaches a model that is accessible anywhere in the assembly: a type nested in
/// another one works as long as it and every type containing it are public, internal or
/// protected internal, issue #207. A <c>private</c>, <c>protected</c> or <c>private protected</c>
/// nested type, or one inside such a type, is out of reach. So is a <c>file</c>-local type, or
/// one nested inside it, because the validator is declared in another file.
/// <para>
/// A generic model, or one declared inside a generic type, is not supported: the validator is a
/// non-generic class, so it has no type parameters to name the model with, issue #219. Full
/// support for generic models is tracked in issue #238; it relaxes <see cref="IsGeneric"/>.
/// </para>
/// </remarks>
internal static class GeneratedValidatorReach
{
    /// <summary>
    /// Whether a validator is generated for <paramref name="model"/>: it is not generic, and the
    /// generated validator can refer to it.
    /// </summary>
    public static bool HasGeneratedValidator(INamedTypeSymbol model, Compilation compilation) =>
        !IsGeneric(model) && CanReach(model, compilation);

    /// <summary>
    /// Whether <paramref name="model"/> or a type containing it declares type parameters. A
    /// constructed form such as <c>Box&lt;int&gt;</c> counts too: it has no validator either.
    /// </summary>
    public static bool IsGeneric(INamedTypeSymbol model)
    {
        for (INamedTypeSymbol? type = model; type is not null; type = type.ContainingType)
        {
            if (type.Arity > 0)
                return true;
        }
        return false;
    }

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
    /// The collected models that have a generated validator. A companion generator's transform
    /// marks every other one as <see langword="null"/>.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> WithGeneratedValidator(ImmutableArray<INamedTypeSymbol?> candidates)
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
