using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// The type parameters generated code declares to name a generic model, issue #238: the
/// validator of <c>Page&lt;TItem&gt;</c> is <c>PageValidator&lt;TItem&gt; where TItem : class</c>,
/// and each probe compiling its calls is generic over the same type parameters. They are the
/// model's own type parameters and those of every type containing it, outermost first, with their
/// declared names, so every type the emitters write with
/// <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>, such as <c>global::Ns.Page&lt;TItem&gt;</c>
/// or <c>global::Ns.Line&lt;TItem&gt;</c>, is valid inside the declaration as it is.
/// </summary>
internal static class GenericSignature
{
    // A constraint type is written with its nullable annotation, as it was declared:
    // where T : IComparable<T>? differs from where T : IComparable<T>.
    private static readonly SymbolDisplayFormat ConstraintFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// The type parameters to declare so <paramref name="type"/> can be named: none for a closed
    /// type, the model's and its containing types' for a generic model as declared, and for a type
    /// written in terms of a model's type parameters, such as a base type <c>Base&lt;TItem&gt;</c>
    /// of <c>Page&lt;TItem&gt;</c>, those of that model.
    /// </summary>
    public static List<ITypeParameterSymbol> TypeParameters(INamedTypeSymbol type)
    {
        var mentioned = new List<ITypeParameterSymbol>();
        Collect(type, mentioned);
        if (mentioned.Count == 0)
            return mentioned;

        // Every type parameter a model's walk mentions belongs to the model or to a type containing
        // it, so the chain of the innermost declaring type holds them all.
        for (var i = 0; i < mentioned.Count; i++)
        {
            if (mentioned[i].DeclaringType is not { } owner)
                continue;
            var chain = Chain(owner);
            if (ContainsAll(chain, mentioned))
                return chain;
        }
        return mentioned;
    }

    /// <summary><c>&lt;TItem, TKey&gt;</c>, or empty when <paramref name="parameters"/> is empty.</summary>
    public static string ParameterList(List<ITypeParameterSymbol> parameters)
    {
        if (parameters.Count == 0)
            return "";

        var sb = new StringBuilder("<");
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(GeneratedCalls.Identifier(parameters[i].Name));
        }
        return sb.Append('>').ToString();
    }

    /// <summary>
    /// One <c>where</c> clause per type parameter that has constraints, copied clause by clause:
    /// <c>class</c>, <c>class?</c>, <c>struct</c>, <c>unmanaged</c>, <c>notnull</c>, the constraint
    /// types with their nullable annotations, <c>new()</c> and <c>allows ref struct</c>.
    /// </summary>
    public static List<string> ConstraintClauses(List<ITypeParameterSymbol> parameters)
    {
        var clauses = new List<string>();
        for (var p = 0; p < parameters.Count; p++)
        {
            var parameter = parameters[p];
            var constraints = new List<string>();
            if (parameter.HasReferenceTypeConstraint)
                constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            else if (parameter.HasUnmanagedTypeConstraint)
                constraints.Add("unmanaged");
            else if (parameter.HasValueTypeConstraint)
                constraints.Add("struct");
            else if (parameter.HasNotNullConstraint)
                constraints.Add("notnull");

            for (var i = 0; i < parameter.ConstraintTypes.Length; i++)
            {
                var constraint = parameter.ConstraintTypes[i].WithNullableAnnotation(parameter.ConstraintNullableAnnotations[i]);
                constraints.Add(constraint.ToDisplayString(ConstraintFormat));
            }

            // A struct constraint implies new(), which may not be written beside it.
            if (parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint)
                constraints.Add("new()");
            if (parameter.AllowsRefLikeType)
                constraints.Add("allows ref struct");

            if (constraints.Count > 0)
                clauses.Add($"where {GeneratedCalls.Identifier(parameter.Name)} : {string.Join(", ", constraints)}");
        }
        return clauses;
    }

    /// <summary>
    /// The type parameters of <paramref name="type"/> and of every type containing it, outermost
    /// first, as its definition declares them.
    /// </summary>
    private static List<ITypeParameterSymbol> Chain(INamedTypeSymbol type)
    {
        var chain = new List<ITypeParameterSymbol>();
        var arguments = GeneratedValidatorNames.TypeArguments(type.OriginalDefinition);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] is ITypeParameterSymbol parameter)
                chain.Add(parameter);
        }
        return chain;
    }

    private static bool ContainsAll(List<ITypeParameterSymbol> chain, List<ITypeParameterSymbol> mentioned)
    {
        for (var i = 0; i < mentioned.Count; i++)
        {
            var parameter = mentioned[i];
            if (!chain.Exists(p => SymbolEqualityComparer.Default.Equals(p, parameter)))
                return false;
        }
        return true;
    }

    private static void Collect(ITypeSymbol type, List<ITypeParameterSymbol> found)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                if (!found.Exists(p => SymbolEqualityComparer.Default.Equals(p, parameter)))
                    found.Add(parameter);
                break;
            case IArrayTypeSymbol array:
                Collect(array.ElementType, found);
                break;
            case INamedTypeSymbol named:
                if (named.ContainingType is { } container)
                    Collect(container, found);
                foreach (var argument in named.TypeArguments)
                    Collect(argument, found);
                break;
        }
    }
}
