using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Decides whether a <c>[Validate]</c> model gets a generated validator at all, shared by
/// ValidatorGenerator, which reports ZV0025 for a model it cannot reach, ZV0029 for a generic
/// one and ZV0031 for one whose validator name another model's takes, and generates nothing for
/// any of them, and by the Inject, Options and ASP.NET Core generators and the nested-validator
/// composition, which then leave the model out, so they never refer to a validator that does not
/// exist, issues #216, #219 and #220.
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
/// <para>
/// Two models whose validators would get the same name in the same namespace, such as
/// <c>Outer.Request</c> and a top-level <c>Outer_Request</c>, get none, issue #220: see
/// <see cref="ValidatorNameClashes"/>.
/// </para>
/// </remarks>
internal static class GeneratedValidatorReach
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

    /// <summary>
    /// Whether a validator is generated for <paramref name="model"/>: it is not generic, the
    /// generated validator can refer to it, and no other model's validator takes its name.
    /// </summary>
    public static bool HasGeneratedValidator(INamedTypeSymbol model, Compilation compilation) =>
        GetsValidatorUnlessNameClashes(model, compilation)
        && ValidatorNameClashes(model, compilation).Count == 0;

    /// <summary>
    /// The other <c>[Validate]</c> models of this compilation whose validator would have the same
    /// name as the one for <paramref name="model"/>, in the same namespace, ordered by name. A
    /// nested model's validator joins its containing types' names with underscores, so
    /// <c>Outer.Request</c> and a top-level <c>Outer_Request</c> both map to
    /// <c>Outer_RequestValidator</c>, and <c>A.B_Request</c> and <c>A_B.Request</c> both map to
    /// <c>A_B_RequestValidator</c>. A model that gets no validator anyway, being generic or out of
    /// reach, takes no name and is not listed.
    /// </summary>
    public static List<INamedTypeSymbol> ValidatorNameClashes(INamedTypeSymbol model, Compilation compilation)
    {
        var clashes = new List<INamedTypeSymbol>();
        var ns = model.ContainingNamespace;
        if (ns is null)
            return clashes;

        var validatorName = GeneratedValidatorNames.ValidatorName(model);
        var joinedName = validatorName.Substring(0, validatorName.Length - "Validator".Length);
        var candidates = new List<INamedTypeSymbol>();
        FindTypesJoinedAs(ns, joinedName, candidates);

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!SymbolEqualityComparer.Default.Equals(candidate, model)
                && SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, compilation.Assembly)
                && HasValidateAttribute(candidate)
                && GetsValidatorUnlessNameClashes(candidate, compilation))
                clashes.Add(candidate);
        }

        clashes.Sort(static (a, b) => string.CompareOrdinal(a.ToDisplayString(), b.ToDisplayString()));
        return clashes;
    }

    private static bool GetsValidatorUnlessNameClashes(INamedTypeSymbol model, Compilation compilation) =>
        !IsGeneric(model) && CanReach(model, compilation);

    /// <summary>
    /// Adds every type in <paramref name="container"/> whose name, joined to the names of the
    /// types containing it below <paramref name="container"/> with underscores, is
    /// <paramref name="joinedName"/>. Each underscore may separate two type names or belong to
    /// one, so every split is looked up by name.
    /// </summary>
    private static void FindTypesJoinedAs(INamespaceOrTypeSymbol container, string joinedName, List<INamedTypeSymbol> found)
    {
        found.AddRange(container.GetTypeMembers(joinedName));

        for (var i = joinedName.IndexOf('_'); i >= 0; i = joinedName.IndexOf('_', i + 1))
        {
            if (i == 0 || i == joinedName.Length - 1)
                continue;
            foreach (var outer in container.GetTypeMembers(joinedName.Substring(0, i)))
                FindTypesJoinedAs(outer, joinedName.Substring(i + 1), found);
        }
    }

    private static bool HasValidateAttribute(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

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
