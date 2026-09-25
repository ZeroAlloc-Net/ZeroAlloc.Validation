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
/// validator cannot read or reach are left out, exactly as for every rule.
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
            if (member is not IPropertySymbol prop)
                continue;

            var validated = ValidatedModel(prop, compilation);
            var validateWith = ValidateWithType(prop);
            if (validateWith is not null && GeneratedValidatorNamedBy(prop, validateWith, compilation) is null)
                result.Add((prop, validateWith, true, validated));
            else if (validated is not null)
                result.Add((prop, validated, false, validated));
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
    /// directly or as its collection element type, or <see langword="null"/>.
    /// </summary>
    private static INamedTypeSymbol? ValidatedModel(IPropertySymbol prop, Compilation compilation)
    {
        if (prop.Type is INamedTypeSymbol nested && HasGeneratedValidator(nested, compilation))
            return (INamedTypeSymbol)nested.OriginalDefinition;
        if (CollectionElementType(prop.Type) is INamedTypeSymbol element && HasGeneratedValidator(element, compilation))
            return (INamedTypeSymbol)element.OriginalDefinition;
        return null;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is a <c>[Validate]</c> model that gets a generated validator.
    /// One the generated validator cannot reach, ZV0025, or a generic one, ZV0029, gets none and
    /// is never a constructor dependency, #216 and #219.
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
