using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// The fast path in front of <see cref="MethodCallProbe"/>: calls whose shape leaves the compiler
/// no choice, so they certainly compile and need no probe. It only ever accepts. Anything it
/// does not recognise goes to the probe, which decides exactly, so a call it passes over is
/// never rejected on its account.
/// <para>
/// A call qualifies when its name belongs to exactly one member across the model, its base types
/// and its interfaces, and that member is an ordinary, non-generic, non-static instance method
/// that the validator's assembly can access and whose metadata the compiler fully supports, with
/// no attributes, no <c>ref</c>, <c>in</c>, <c>out</c>, optional or <c>params</c> parameters, no
/// pointer or function-pointer parameter types, which need an unsafe context the validator does
/// not have, one parameter per argument whose type is identical to the argument's, nullability
/// included, and a <c>bool</c> result returned by value.
/// Member lookup then finds that method alone, it is applicable by identity, and overload
/// resolution has nothing to choose between. Extension methods cannot interfere: C# looks for
/// them only when no applicable instance method is found. Attributes are excluded because some,
/// such as <c>[Obsolete(error: true)]</c>, <c>[Experimental]</c> or
/// <c>[UnmanagedCallersOnly]</c>, make a well-formed call an error.
/// </para>
/// </summary>
internal static class CertainCall
{
    /// <summary>
    /// The method <c>instance.Name()</c>, or <c>instance.Name(value)</c> with a value of
    /// <paramref name="argumentType"/>, certainly binds to when used as a condition, or
    /// <see langword="null"/> when the probe has to decide.
    /// </summary>
    public static IMethodSymbol? Condition(Compilation compilation, INamedTypeSymbol model, string name, ITypeSymbol? argumentType)
    {
        if (SoleMember(model, name) is not IMethodSymbol method) return null;
        if (!IsPlainInstanceMethod(compilation, method) || method.GetAttributes().Length != 0) return null;
        if (method.ReturnType.SpecialType != SpecialType.System_Boolean) return null;

        if (argumentType is null) return method.Parameters.Length == 0 ? method : null;
        if (method.Parameters.Length != 1) return null;

        var parameter = method.Parameters[0];
        if (parameter.RefKind != RefKind.None || parameter.IsParams || parameter.IsOptional) return null;
        // A pointer needs an unsafe context, which the generated validator does not have.
        if (ContainsPointer(parameter.Type)) return null;
        return SymbolEqualityComparer.IncludeNullability.Equals(parameter.Type, argumentType) ? method : null;
    }

    /// <summary>
    /// Whether <c>instance.Check()</c> certainly binds to <paramref name="method"/>, a
    /// <c>[CustomValidation]</c> method whose signature ZV0013 accepts: it is the only member of
    /// that name, and carries no attribute but <c>[CustomValidation]</c>.
    /// </summary>
    public static bool CustomValidation(Compilation compilation, INamedTypeSymbol model, IMethodSymbol method) =>
        SoleMember(model, method.Name) is IMethodSymbol sole
        && SymbolEqualityComparer.Default.Equals(sole, method)
        && IsPlainInstanceMethod(compilation, method)
        && method.Parameters.Length == 0
        && method.GetAttributes().Length == 1;

    private static bool IsPlainInstanceMethod(Compilation compilation, IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary
        && !method.HasUnsupportedMetadata
        && !method.IsStatic
        && !method.IsGenericMethod
        && !method.ReturnsByRef
        && !method.ReturnsByRefReadonly
        && compilation.IsSymbolAccessibleWithin(method, compilation.Assembly);

    private static bool ContainsPointer(ITypeSymbol type) => type switch
    {
        IPointerTypeSymbol or IFunctionPointerTypeSymbol => true,
        IArrayTypeSymbol array => ContainsPointer(array.ElementType),
        INamedTypeSymbol named => AnyContainsPointer(named.TypeArguments),
        _ => false,
    };

    private static bool AnyContainsPointer(System.Collections.Immutable.ImmutableArray<ITypeSymbol> types)
    {
        foreach (var type in types)
        {
            if (ContainsPointer(type)) return true;
        }
        return false;
    }

    /// <summary>
    /// The one member named <paramref name="name"/> on <paramref name="model"/>, its base types
    /// and its interfaces, or <see langword="null"/> when there is none or more than one.
    /// </summary>
    private static ISymbol? SoleMember(INamedTypeSymbol model, string name)
    {
        ISymbol? found = null;
        for (var type = model; type is not null; type = type.BaseType)
        {
            if (!TryAdd(type, name, ref found)) return null;
        }
        foreach (var implemented in model.AllInterfaces)
        {
            if (!TryAdd(implemented, name, ref found)) return null;
        }
        return found;
    }

    private static bool TryAdd(INamedTypeSymbol type, string name, ref ISymbol? found)
    {
        foreach (var member in type.GetMembers(name))
        {
            if (found is not null) return false;
            found = member;
        }
        return true;
    }
}
