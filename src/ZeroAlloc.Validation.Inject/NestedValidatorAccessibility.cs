using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Shared by ValidatorGenerator, InjectGenerator, AspNetCoreFilterEmitter and
/// OptionsValidationEmitter to decide whether a <c>[Validate]</c> model's generated
/// <c>{Model}Validator</c> would be emitted <c>public</c> when
/// <c>ZeroAllocGeneratedAccessibility</c> is unset or <c>Public</c> (issue #193 point 3,
/// extending the #184 rule): the model itself must be effectively public, AND every nested
/// <c>[Validate]</c> model it takes as a constructor-injected validator dependency — a scalar
/// property or a collection element, but not an explicit <c>[ValidateWith]</c> override, whose
/// target type's accessibility is the caller's own responsibility, not ours — must itself
/// resolve to a public validator too, computed transitively over the model graph. Without this,
/// a public model with an internal nested <c>[Validate]</c> model's validator took an internal
/// constructor parameter on a public constructor and failed with CS0051.
/// </summary>
/// <remarks>
/// Walks a property's declaring type and its base chain without reproducing
/// ValidatorGenerator.MemberWalker's hiding/<c>[Validate(IncludeBaseProperties = false)]</c>
/// handling — that project is not referenced here, by design, to avoid loading an extra
/// assembly into each of these four independent generators (the same reason
/// ValidatorRegistrationEmitter.cs is shared as a linked file rather than referenced). Walking
/// more properties than the real constructor actually injects only makes this check MORE
/// conservative (occasionally Internal where Public would also have compiled); the one
/// direction that must never happen is treating a validator as Public when its real generated
/// constructor takes an Internal parameter, and walking fewer properties could cause that.
/// </remarks>
public static class NestedValidatorAccessibility
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";
    private const string ValidateWithAttributeFqn = "ZeroAlloc.Validation.ValidateWithAttribute";

    public static bool WouldBePublic(INamedTypeSymbol model, Compilation compilation) =>
        WouldBePublic(model, compilation, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));

    private static bool WouldBePublic(INamedTypeSymbol model, Compilation compilation, HashSet<INamedTypeSymbol> inProgress)
    {
        if (!IsEffectivelyPublic(model))
            return false;

        // Cycle guard: a model already being walked on this path is not re-evaluated — its own
        // resolution does not depend on this particular recursive call reaching it again.
        if (!inProgress.Add(model))
            return true;

        try
        {
            foreach (var prop in AllProperties(model))
            {
                // [ValidateWith] overrides auto-compose; the specified validator type's own
                // accessibility is the caller's responsibility, not something we compute here.
                if (HasValidateWithAttribute(prop))
                    continue;

                if (prop.Type is INamedTypeSymbol nested && IsValidatorDependency(nested, compilation))
                {
                    if (!WouldBePublic(nested, compilation, inProgress)) return false;
                    continue;
                }

                if (GetCollectionElementType(prop.Type) is INamedTypeSymbol element && IsValidatorDependency(element, compilation))
                {
                    if (!WouldBePublic(element, compilation, inProgress)) return false;
                }
            }

            return true;
        }
        finally
        {
            inProgress.Remove(model);
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

    private static bool IsEffectivelyPublic(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility != Accessibility.Public)
                return false;
        }
        return true;
    }

    // A [Validate] type that gets no validator, one the generated validator cannot reach, ZV0025,
    // or a generic one, ZV0029, is never a constructor dependency and must not make the outer
    // validator internal, #216 and #219.
    private static bool IsValidatorDependency(INamedTypeSymbol type, Compilation compilation) =>
        HasValidateAttribute(type) && GeneratedValidatorReach.HasGeneratedValidator(type, compilation);

    private static bool HasValidateAttribute(INamedTypeSymbol type) =>
        type.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, System.StringComparison.Ordinal));

    private static bool HasValidateWithAttribute(IPropertySymbol prop) =>
        prop.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), ValidateWithAttributeFqn, System.StringComparison.Ordinal));

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
