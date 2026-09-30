using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Decides whether a <c>[Validate]</c> model gets a generated validator at all, shared by
/// ValidatorGenerator, which reports ZV0025 for a model it cannot reach, ZV0029 for a generic one
/// whose type parameters the validator cannot redeclare and ZV0031 for one whose validator name
/// another model's takes, and generates nothing for any of them, and by the Inject, Options and
/// ASP.NET Core generators and the nested-validator composition, which then leave the model out,
/// so they never refer to a validator that does not exist, issues #216, #219 and #220.
/// </summary>
/// <remarks>
/// The validator is a top-level class in the model's namespace, declared in its own generated
/// file. It reaches a model that is accessible anywhere in the assembly: a type nested in
/// another one works as long as it and every type containing it are public, internal or
/// protected internal, issue #207. A <c>private</c>, <c>protected</c> or <c>private protected</c>
/// nested type, or one inside such a type, is out of reach. So is a <c>file</c>-local type, or
/// one nested inside it, because the validator is declared in another file.
/// <para>
/// A generic model, or one declared inside a generic type, gets a generic validator that
/// redeclares the type parameters of the model and of every containing type, with their declared
/// names, issue #238. That fails only for a type parameter the validator cannot declare, see
/// <see cref="UnsupportedTypeParameter(INamedTypeSymbol, out TypeParameterClash)"/>: one whose name
/// repeats along the containing chain, <c>Outer&lt;T&gt;.Inner&lt;T&gt;</c>, or one named like the
/// validator or like one of its members. The companion generators never list a generic model as a
/// root of their registrations, <see cref="IsGeneric"/>: nothing closed can be registered for it,
/// and its closings reach the container through the models that compose them.
/// </para>
/// <para>
/// Two models whose validators would get the same name in the same namespace, such as
/// <c>Outer.Request</c> and a top-level <c>Outer_Request</c>, get none, issue #220: see
/// <see cref="ValidatorNameClashes"/>. A generic validator's arity is part of its name, so
/// <c>Box</c> and <c>Box&lt;T&gt;</c> do not clash.
/// </para>
/// </remarks>
internal static class GeneratedValidatorReach
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

    /// <summary>
    /// Whether a validator is generated for <paramref name="model"/>: its type parameters can be
    /// redeclared, the generated validator can refer to it, and no other model's validator takes
    /// its name. A closing of a generic model, such as <c>Page&lt;Order&gt;</c>, has one when the
    /// model has: the model's generated validator closed over the same type arguments.
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
    /// <c>A_B_RequestValidator</c>. A model that gets no validator anyway, having type parameters
    /// the validator cannot redeclare or being out of reach, takes no name and is not listed. The
    /// arity of a generic validator is part of its name, so only a model whose validator has the
    /// same total arity clashes: <c>Box</c> and <c>Box&lt;T&gt;</c> get <c>BoxValidator</c> and
    /// <c>BoxValidator&lt;T&gt;</c>, while <c>Outer&lt;T&gt;.Inner</c> and a top-level
    /// <c>Outer_Inner&lt;T&gt;</c> both get <c>Outer_InnerValidator&lt;T&gt;</c>. A closing is
    /// checked as the model it closes.
    /// </summary>
    public static List<INamedTypeSymbol> ValidatorNameClashes(INamedTypeSymbol model, Compilation compilation)
    {
        var clashes = new List<INamedTypeSymbol>();
        model = model.OriginalDefinition;
        var ns = model.ContainingNamespace;
        if (ns is null)
            return clashes;

        var validatorName = GeneratedValidatorNames.ValidatorName(model);
        var arity = GeneratedValidatorNames.TotalArity(model);
        var joinedName = validatorName.Substring(0, validatorName.Length - "Validator".Length);
        var candidates = new List<INamedTypeSymbol>();
        FindTypesJoinedAs(ns, joinedName, candidates);

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!SymbolEqualityComparer.Default.Equals(candidate, model)
                && GeneratedValidatorNames.TotalArity(candidate) == arity
                && SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, compilation.Assembly)
                && HasValidateAttribute(candidate)
                && GetsValidatorUnlessNameClashes(candidate, compilation))
                clashes.Add(candidate);
        }

        clashes.Sort(static (a, b) => string.CompareOrdinal(a.ToDisplayString(), b.ToDisplayString()));
        return clashes;
    }

    private static bool GetsValidatorUnlessNameClashes(INamedTypeSymbol model, Compilation compilation) =>
        UnsupportedTypeParameter(model) is null && CanReach(model, compilation);

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
    /// Whether <paramref name="model"/> or a type containing it declares type parameters, so its
    /// validator is generic. A constructed form such as <c>Box&lt;int&gt;</c> counts too. The
    /// companion generators leave such a model out of the roots they register: nothing closed
    /// can be registered for the model as declared.
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

    /// <summary>
    /// The type parameter of <paramref name="model"/> or of a type containing it that the
    /// generated validator cannot redeclare, or <see langword="null"/> when there is none. ZV0029
    /// reports it. See the overload that also says why.
    /// </summary>
    public static ITypeParameterSymbol? UnsupportedTypeParameter(INamedTypeSymbol model) =>
        UnsupportedTypeParameter(model, out _);

    /// <summary>
    /// The type parameter of <paramref name="model"/> or of a type containing it that the
    /// generated validator cannot redeclare, or <see langword="null"/> when there is none, and in
    /// <paramref name="clash"/> why:
    /// <list type="bullet">
    /// <item><see cref="TypeParameterClash.Repeated"/>: a type parameter further out along the
    /// containing chain has the same name, the CS0693 case <c>Outer&lt;T&gt;.Inner&lt;T&gt;</c>,
    /// which the validator's one type parameter list would declare twice, CS0692.</item>
    /// <item><see cref="TypeParameterClash.ValidatorName"/>: it is named like the validator
    /// itself, CS0694.</item>
    /// <item><see cref="TypeParameterClash.MemberName"/>: it is named like a member of the
    /// validator, with which it shares a declaration space, CS0102, issue #299. See
    /// <see cref="IsValidatorMemberName"/>.</item>
    /// </list>
    /// ZV0029 reports it.
    /// </summary>
    public static ITypeParameterSymbol? UnsupportedTypeParameter(INamedTypeSymbol model, out TypeParameterClash clash)
    {
        clash = TypeParameterClash.None;
        model = model.OriginalDefinition;
        if (!IsGeneric(model))
            return null;

        var validatorName = GeneratedValidatorNames.ValidatorName(model);
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? type = model; type is not null; type = type.ContainingType)
            chain.Insert(0, type);

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < chain.Count; i++)
        {
            foreach (var parameter in chain[i].TypeParameters)
            {
                clash = !names.Add(parameter.Name) ? TypeParameterClash.Repeated
                    : string.Equals(parameter.Name, validatorName, StringComparison.Ordinal) ? TypeParameterClash.ValidatorName
                    : IsValidatorMemberName(parameter.Name) ? TypeParameterClash.MemberName
                    : TypeParameterClash.None;
                if (clash != TypeParameterClash.None)
                    return parameter;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether a generated validator declares, or may declare, a member named
    /// <paramref name="name"/>: <c>Validate</c> and <c>ValidateAsync</c>; a name starting with two
    /// underscores, the prefix of its private members such as <c>__ValidateAsyncCore</c>,
    /// <c>__Rule_Name_0</c>, <c>__Regex_Name</c> and the <c>__validateNext</c> delegate caches of its
    /// behavior chains, which the C# specification reserves for the implementation anyway; or <c>_</c> + a name + <c>Validator</c>, the form of the field that
    /// holds a nested model's validator, <c>_addressValidator</c>. Which of these members a
    /// validator declares depends on the model's rules and properties, so every name of these
    /// forms is reserved, and a model does not start failing when it gains a rule or a property.
    /// A parameter or local of a generated method may share a type parameter's name, so those
    /// names are not reserved.
    /// </summary>
    private static bool IsValidatorMemberName(string name) =>
        string.Equals(name, "Validate", StringComparison.Ordinal)
        || string.Equals(name, "ValidateAsync", StringComparison.Ordinal)
        || name.StartsWith("__", StringComparison.Ordinal)
        || (name.Length > "_Validator".Length
            && name[0] == '_'
            && name.EndsWith("Validator", StringComparison.Ordinal));

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
}
