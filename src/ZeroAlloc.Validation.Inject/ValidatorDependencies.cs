using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// The validators a <c>[Validate]</c> model's generated validator takes in its constructor, one
/// per nested or collection property. This is the one routine that decides them: RuleEmitter
/// declares the constructor from it, NestedValidatorAccessibility decides the validator's
/// accessibility from it, and ValidatorRegistrationEmitter registers what it lists so a container
/// can build a composed validator, issue #246.
/// </summary>
/// <remarks>
/// <para>
/// The properties are the ones <see cref="MemberWalker.GetMembersIncludingBase"/> yields: hidden
/// members, base members under <c>[Validate(IncludeBaseProperties = false)]</c>, and members the
/// validator cannot read or reach are left out, exactly as for every rule. So is a property
/// <see cref="ObsoleteErrors.IsObsoleteError"/> finds <c>[Obsolete(error: true)]</c>, whose
/// read would raise CS0619 in the generated file, issue #267.
/// </para>
/// <para>
/// A property with <c>[ValidateWith(typeof(X))]</c> is validated by <c>X</c>, and the constructor
/// takes <c>X</c> itself: the attribute names one validator, and two properties of the same type
/// may name different ones. Any other property whose type, or collection element type, is a
/// <c>[Validate]</c> model with a generated validator is validated by that model's validator, and
/// the constructor takes it as <c>ValidatorFor&lt;TModel&gt;</c>, the service type every generated
/// validator is registered under.
/// </para>
/// <para>
/// <c>[ValidateWith]</c> naming the model's own generated validator from the same compilation,
/// the ZV0011 case, cannot be told apart by type: generator output is not visible to the
/// generators, so the type is an error type. When its name is the generated validator's name
/// for the property's model or element model, it is that validator, and the property takes the
/// auto-composed path; see <see cref="GeneratedValidatorNamedBy"/>.
/// </para>
/// <para>
/// A property of a generic model's type keeps its type arguments, issue #238: <c>Page&lt;Order&gt;</c>
/// is taken as <c>ValidatorFor&lt;Page&lt;Order&gt;&gt;</c>, and a property of a generic model
/// declared in terms of the model's own type parameters, such as <c>Line&lt;TItem&gt;</c>, as
/// <c>ValidatorFor&lt;Line&lt;TItem&gt;&gt;</c>. A property whose type is a type parameter is
/// composed only through the <c>[Validate]</c> class its constraint names: <c>TItem</c> with
/// <c>where TItem : Address</c> is taken as <c>ValidatorFor&lt;Address&gt;</c>, exactly as a
/// property declared <c>Address</c>. A property that would nest a model inside itself without end,
/// ZV0037, is not composed; see <see cref="ExpandingComposition"/>.
/// </para>
/// </remarks>
internal static class ValidatorDependencies
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";
    private const string ValidateWithAttributeFqn = "ZeroAlloc.Validation.ValidateWithAttribute";

    /// <summary>
    /// Each validator <paramref name="model"/>'s generated constructor takes, in declaration
    /// order, with the property it validates. With <c>IsValidateWith</c> set, <c>Type</c> is the
    /// <c>[ValidateWith]</c> validator the constructor takes as itself; otherwise it is the
    /// <c>[Validate]</c> model the constructor takes <c>ValidatorFor</c> of. <c>Model</c> is the
    /// property's <c>[Validate]</c> model or element model with a generated validator, if any,
    /// whichever path the property takes.
    /// </summary>
    public static List<(IPropertySymbol Property, INamedTypeSymbol Type, bool IsValidateWith, INamedTypeSymbol? Model)>
        Of(INamedTypeSymbol model, Compilation compilation)
    {
        var result = new List<(IPropertySymbol, INamedTypeSymbol, bool, INamedTypeSymbol?)>();
        foreach (var member in MemberWalker.GetMembersIncludingBase(model, compilation))
        {
            // Reading an [Obsolete(error: true)] property raises CS0619, which pragma cannot
            // suppress, so the generated validator never validates it and takes no validator for
            // it, issue #267; ZV0032 reports it instead.
            if (member is not IPropertySymbol prop || ObsoleteErrors.IsObsoleteError(prop))
                continue;

            var validated = ValidatedModel(prop, compilation);
            var validateWith = ValidateWithType(prop);
            if (validateWith is not null && GeneratedValidatorNamedBy(prop, validateWith, compilation) is null)
                result.Add((prop, validateWith, true, validated));
            else if (validated is not null && !ExpandingComposition.IsExpanding(model, prop, compilation))
                result.Add((prop, validated, false, validated));
        }
        return result;
    }

    /// <summary>
    /// Each property of <paramref name="model"/> that <see cref="Of"/> would take the generated
    /// validator of its <c>[Validate]</c> model for, with that model, before
    /// <see cref="ExpandingComposition"/> leaves out the ones that nest a model inside itself
    /// without end, which it finds from these.
    /// </summary>
    internal static List<(IPropertySymbol Property, INamedTypeSymbol Model)> AutoComposedCandidates(INamedTypeSymbol model, Compilation compilation)
    {
        var result = new List<(IPropertySymbol, INamedTypeSymbol)>();
        foreach (var member in MemberWalker.GetMembersIncludingBase(model, compilation))
        {
            if (member is not IPropertySymbol prop || ObsoleteErrors.IsObsoleteError(prop))
                continue;

            var validated = ValidatedModel(prop, compilation);
            if (validated is null)
                continue;
            var validateWith = ValidateWithType(prop);
            if (validateWith is null || GeneratedValidatorNamedBy(prop, validateWith, compilation) is not null)
                result.Add((prop, validated));
        }
        return result;
    }

    /// <summary>
    /// The model whose generated validator <paramref name="validateWith"/>, named by
    /// <c>[ValidateWith]</c> on <paramref name="prop"/>, is when that validator is generated in
    /// this same compilation, otherwise <see langword="null"/>. It is then an error type to every
    /// generator, so it is recognised by name: the generated validator's name for the property's
    /// model or element model.
    /// </summary>
    public static INamedTypeSymbol? GeneratedValidatorNamedBy(IPropertySymbol prop, INamedTypeSymbol validateWith, Compilation compilation)
    {
        if (validateWith.TypeKind != TypeKind.Error)
            return null;
        return ValidatedModel(prop, compilation) is { } model
               && SymbolEqualityComparer.Default.Equals(model.ContainingAssembly, compilation.Assembly)
               && string.Equals(validateWith.Name, GeneratedValidatorNames.ValidatorName(model), System.StringComparison.Ordinal)
            ? model
            : null;
    }

    /// <summary>
    /// The <c>[Validate]</c> model with a generated validator that <paramref name="prop"/> holds,
    /// directly or as its collection element type, or <see langword="null"/>. A closing of a
    /// generic model is returned as it is, <c>Page&lt;Order&gt;</c>, not as the model's
    /// definition, <c>Page&lt;TItem&gt;</c>, whose type parameters do not exist where the
    /// property is validated; see <see cref="ComposedModel"/>.
    /// </summary>
    private static INamedTypeSymbol? ValidatedModel(IPropertySymbol prop, Compilation compilation) =>
        ComposedModel(prop.Type, compilation)
        ?? (CollectionElementType(prop.Type) is { } element ? ComposedModel(element, compilation) : null);

    /// <summary>
    /// The <c>[Validate]</c> model a value of <paramref name="type"/> is validated as, or
    /// <see langword="null"/> when it is not validated as one: the type itself when it is a model
    /// with a generated validator, closings of generic models included, and for a type parameter
    /// the <c>[Validate]</c> class its constraint names, issue #238. A type parameter
    /// unconstrained, or constrained only to other types, is not validated as a nested model: the
    /// validator for whatever it is closed over is not known where the property is validated.
    /// </summary>
    public static INamedTypeSymbol? ComposedModel(ITypeSymbol type, Compilation compilation) =>
        ComposedModel(type, compilation, depth: 0);

    private static INamedTypeSymbol? ComposedModel(ITypeSymbol type, Compilation compilation, int depth)
    {
        switch (type)
        {
            case INamedTypeSymbol named:
                return HasGeneratedValidator(named, compilation) ? named : null;
            case ITypeParameterSymbol parameter when depth < 8:
                // A class constraint can itself be a type parameter, whose constraint is then the
                // model: where T : U where U : Address. C# rejects a cycle of these, so the depth
                // bound only guards against a compilation that already has errors.
                foreach (var constraint in parameter.ConstraintTypes)
                {
                    if (constraint.TypeKind is TypeKind.Class or TypeKind.TypeParameter
                        && ComposedModel(constraint, compilation, depth + 1) is { } model)
                        return model;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="type"/> is a <c>[Validate]</c> model that gets a generated validator.
    /// One the generated validator cannot reach, ZV0025, or one whose type parameters it cannot
    /// redeclare, ZV0029, gets none and is never a constructor dependency, #216 and #219.
    /// </summary>
    public static bool HasGeneratedValidator(INamedTypeSymbol type, Compilation compilation) =>
        type.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, System.StringComparison.Ordinal))
        && GeneratedValidatorReach.HasGeneratedValidator(type, compilation);

    /// <summary>
    /// The validator type the first <c>[ValidateWith]</c> on <paramref name="prop"/> names, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    public static INamedTypeSymbol? ValidateWithType(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (!string.Equals(attr.AttributeClass?.ToDisplayString(), ValidateWithAttributeFqn, System.StringComparison.Ordinal))
                continue;
            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is INamedTypeSymbol t)
                return t;
        }
        return null;
    }

    /// <summary>
    /// The element type of <paramref name="type"/> when it is an array or implements
    /// <c>IEnumerable&lt;T&gt;</c>, otherwise <see langword="null"/>.
    /// </summary>
    public static ITypeSymbol? CollectionElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr)
            return arr.ElementType;

        if (type is not INamedTypeSymbol named)
            return null;

        if (named.IsGenericType && named.TypeArguments.Length == 1
            && string.Equals(named.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.IEnumerable<T>", System.StringComparison.Ordinal))
            return named.TypeArguments[0];

        foreach (var iface in named.AllInterfaces)
        {
            if (iface.IsGenericType && iface.TypeArguments.Length == 1
                && string.Equals(iface.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.IEnumerable<T>", System.StringComparison.Ordinal))
                return iface.TypeArguments[0];
        }

        return null;
    }
}
