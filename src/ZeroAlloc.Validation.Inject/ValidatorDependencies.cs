using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// The validators a <c>[Validate]</c> model's generated validator takes in its constructor, one
/// per nested or collection property, shared by NestedValidatorAccessibility, which decides the
/// validator's accessibility from them, and by ValidatorRegistrationEmitter, which registers them
/// so the container can build a composed validator, issue #246.
/// </summary>
/// <remarks>
/// <para>
/// A property with <c>[ValidateWith(typeof(X))]</c> is validated by <c>X</c>, and the constructor
/// takes <c>X</c> itself: the attribute names one validator, and two properties of the same type
/// may name different ones. Any other property whose type, or collection element type, is a
/// <c>[Validate]</c> model with a generated validator is validated by that model's validator, and
/// the constructor takes it as <c>ValidatorFor&lt;TModel&gt;</c>, so a container resolves it by the
/// same service type every generated validator is registered under. The precedence matches
/// RuleEmitter's.
/// </para>
/// <para>
/// Walks the model's declared type and its base chain without reproducing
/// ValidatorGenerator.MemberWalker's hiding and <c>[Validate(IncludeBaseProperties = false)]</c>
/// handling. That project is not referenced here, by design: each generator that needs this
/// compiles the file in, rather than loading another generator assembly. Walking more properties
/// than the constructor takes only makes each consumer more conservative: an accessibility check
/// that is occasionally internal where public would also compile, or a registration for a
/// validator that is not needed.
/// </para>
/// </remarks>
internal static class ValidatorDependencies
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";
    private const string ValidateWithAttributeFqn = "ZeroAlloc.Validation.ValidateWithAttribute";

    /// <summary>
    /// Each nested validator <paramref name="model"/>'s generated validator takes. With
    /// <c>IsValidateWith</c> set, <c>Type</c> is the <c>[ValidateWith]</c> validator the constructor
    /// takes as itself; otherwise it is the <c>[Validate]</c> model the constructor takes
    /// <c>ValidatorFor</c> of. A type can appear more than once.
    /// </summary>
    public static IEnumerable<(INamedTypeSymbol Type, bool IsValidateWith)> Of(INamedTypeSymbol model, Compilation compilation)
    {
        foreach (var prop in AllProperties(model))
        {
            if (GetValidateWithType(prop) is { } validatorType)
            {
                yield return (validatorType, true);
                continue;
            }

            if (prop.Type is INamedTypeSymbol nested && IsGeneratedValidatorModel(nested, compilation))
            {
                yield return (nested, false);
                continue;
            }

            if (GetCollectionElementType(prop.Type) is INamedTypeSymbol element && IsGeneratedValidatorModel(element, compilation))
                yield return (element, false);
        }
    }

    private static IEnumerable<IPropertySymbol> AllProperties(INamedTypeSymbol type)
    {
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            foreach (var member in t.GetMembers())
            {
                if (member is IPropertySymbol { IsStatic: false } prop)
                    yield return prop;
            }
        }
    }

    // A [Validate] type that gets no validator, one the generated validator cannot reach, ZV0025,
    // or a generic one, ZV0029, is never a constructor dependency, #216 and #219.
    private static bool IsGeneratedValidatorModel(INamedTypeSymbol type, Compilation compilation) =>
        type.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, System.StringComparison.Ordinal))
        && GeneratedValidatorReach.HasGeneratedValidator(type, compilation);

    /// <summary>
    /// The validator type the first <c>[ValidateWith]</c> on <paramref name="prop"/> names, as
    /// RuleEmitter reads it, or <see langword="null"/> when there is none.
    /// </summary>
    private static INamedTypeSymbol? GetValidateWithType(IPropertySymbol prop)
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

    private static ITypeSymbol? GetCollectionElementType(ITypeSymbol type)
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
