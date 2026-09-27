using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Finds properties the generated validator must never read. Unlike CS0612 and CS0618, pragma
/// cannot suppress the CS0619 a read of an <c>[Obsolete(message, error: true)]</c> property
/// raises, so ValidatorGenerator leaves every rule, nested validation and collection
/// validation of such a property out of the generated file and reports ZV0032 as an error
/// instead. Shared with the Inject, Options and ASP.NET Core generators through
/// <see cref="ValidatorDependencies"/>, so the DI glue registers no validator for a property the
/// generated constructor no longer takes, issue #267.
/// </summary>
internal static class ObsoleteErrors
{
    /// <summary>
    /// Whether <paramref name="property"/>, or a property it overrides, or the getter of either,
    /// is <c>[Obsolete(message, error: true)]</c>, so that reading it raises CS0619.
    /// </summary>
    public static bool IsObsoleteError(IPropertySymbol property)
    {
        for (IPropertySymbol? current = property; current is not null; current = current.OverriddenProperty)
        {
            foreach (var attr in current.GetAttributes())
            {
                if (IsObsoleteErrorAttribute(attr)) return true;
            }
            if (current.GetMethod is { } getter)
            {
                foreach (var attr in getter.GetAttributes())
                {
                    if (IsObsoleteErrorAttribute(attr)) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Whether <paramref name="attr"/> is <c>[Obsolete(..., error: true)]</c>. The <c>error</c>
    /// argument, named or positional, is found by parameter name on the constructor the attribute
    /// resolved to, so it does not depend on which <c>ObsoleteAttribute</c> overload was written.
    /// </summary>
    private static bool IsObsoleteErrorAttribute(AttributeData attr)
    {
        if (!string.Equals(attr.AttributeClass?.ToDisplayString(), "System.ObsoleteAttribute", System.StringComparison.Ordinal))
            return false;

        var parameters = attr.AttributeConstructor?.Parameters;
        if (parameters is null) return false;

        var arguments = attr.ConstructorArguments;
        for (int i = 0; i < parameters.Value.Length && i < arguments.Length; i++)
        {
            if (string.Equals(parameters.Value[i].Name, "error", System.StringComparison.Ordinal))
                return arguments[i].Value is true;
        }
        return false;
    }
}
