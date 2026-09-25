using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Decides whether a <c>[Validate]</c> model's generated <c>{Model}Validator</c> is emitted
/// <c>public</c> when <c>ZeroAllocGeneratedAccessibility</c> is unset or <c>Public</c>, issue #193
/// point 3, extending the #184 rule: the model itself must be effectively public, AND every type
/// the validator's public constructor takes must be too. Otherwise the constructor would take a
/// less accessible parameter and fail with CS0051.
/// </summary>
/// <remarks>
/// <para>
/// The constructor takes each nested or collection <c>[Validate]</c> model's validator as
/// <c>ValidatorFor&lt;TModel&gt;</c>, issue #246, so that parameter is exactly as accessible as the
/// nested model: whether the nested model's own validator is public does not matter. Before #246
/// the parameter was the nested validator's own type, and the rule had to be computed
/// transitively over the whole model graph; an internal model anywhere below made every
/// validator above it internal.
/// </para>
/// <para>
/// A <c>[ValidateWith(typeof(X))]</c> property is taken as <c>X</c> itself, so <c>X</c> must be
/// effectively public too, type arguments included. Before #246 that case was left to the
/// caller, and an internal <c>X</c> on a public model failed to compile with CS0051.
/// </para>
/// <para>
/// <see cref="ValidatorDependencies"/> lists the parameters. It can list more properties than the
/// real constructor takes, which only makes this check more conservative; the one direction that
/// must never happen is a public validator whose constructor takes an internal parameter.
/// </para>
/// </remarks>
public static class NestedValidatorAccessibility
{
    public static bool WouldBePublic(INamedTypeSymbol model, Compilation compilation)
    {
        if (!IsEffectivelyPublic(model))
            return false;

        foreach (var dependency in ValidatorDependencies.Of(model, compilation))
        {
            if (!IsEffectivelyPublic(dependency.Type))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="type"/>, every type containing it and every type argument it is
    /// constructed with are public, so a public signature can name it.
    /// </summary>
    private static bool IsEffectivelyPublic(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsEffectivelyPublic(array.ElementType);
            case ITypeParameterSymbol:
                return true;
            case INamedTypeSymbol named:
                for (INamedTypeSymbol? t = named; t is not null; t = t.ContainingType)
                {
                    if (t.DeclaredAccessibility != Accessibility.Public)
                        return false;
                    foreach (var argument in t.TypeArguments)
                    {
                        if (!IsEffectivelyPublic(argument))
                            return false;
                    }
                }
                return true;
            default:
                return type.DeclaredAccessibility == Accessibility.Public;
        }
    }
}
