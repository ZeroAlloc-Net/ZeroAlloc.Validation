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
/// <c>[UnmanagedCallersOnly]</c>, make a well-formed call an error. The same attributes on a
/// type containing the method are excluded as well.
/// </para>
/// <para>
/// A call that compiles can still warn, and a warning in the generated validator is mirrored
/// as ZV0032, so <see cref="MethodCallProbe.CallWarnings"/> compiles the validator's body
/// unless every call in it certainly cannot warn. <see cref="ConditionCannotWarn"/>,
/// <see cref="RuleCallCannotWarn"/> and <see cref="CustomValidation"/> decide that for each
/// call, on top of the checks above: a nullability attribute on the parameter or the property,
/// a parameter whose nullability differs from the property's, or an earlier rule that tests
/// the property for null, which leaves it maybe-null, each sends the call to the probe.
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
    /// Whether the call <see cref="Condition"/> accepts certainly raises no warning in the
    /// generated validator either. <paramref name="argument"/> is the property whose value the
    /// call receives, or <see langword="null"/> when it takes none; then nothing can warn, as
    /// the method carries no attribute. With an argument, the parameter must carry no
    /// attribute, such as <c>[DisallowNull]</c>, the property no nullability attribute, such as
    /// <c>[MaybeNull]</c>, and no earlier rule may test the property for null:
    /// <paramref name="argumentNullTested"/>.
    /// </summary>
    public static bool ConditionCannotWarn(
        Compilation compilation, INamedTypeSymbol model, string name, IPropertySymbol? argument, bool argumentNullTested)
    {
        if (Condition(compilation, model, name, argument?.Type) is not { } method) return false;
        if (argument is null) return true;
        return !argumentNullTested
            && method.Parameters[0].GetAttributes().Length == 0
            && !HasNullabilityAttribute(argument);
    }

    /// <summary>
    /// Whether a custom rule's call, <c>__Rule.IsValid(instance.Property)</c>, certainly raises
    /// no warning: <c>ValidationAttribute&lt;T&gt;.IsValid(T value)</c> takes
    /// <paramref name="valueType"/>, the <c>T</c> of <paramref name="ruleClass"/>, which must be
    /// the property's type exactly, nullability included. The rule and its base types must not
    /// be obsolete or experimental, the property must carry no nullability attribute, and no
    /// earlier rule may test it for null.
    /// </summary>
    public static bool RuleCallCannotWarn(INamedTypeSymbol ruleClass, ITypeSymbol valueType, IPropertySymbol property, bool nullTested)
    {
        if (nullTested || HasNullabilityAttribute(property)) return false;
        if (!SymbolEqualityComparer.IncludeNullability.Equals(valueType, property.Type)) return false;
        for (var type = ruleClass; type is not null; type = type.BaseType)
        {
            if (IsInDeprecatedType(type)) return false;
        }
        return true;
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
        && !IsInDeprecatedType(method.ContainingType)
        && compilation.IsSymbolAccessibleWithin(method, compilation.Assembly);

    /// <summary>
    /// Whether <paramref name="type"/> or a type containing it is <c>[Obsolete]</c> or
    /// <c>[Experimental]</c>, either of which can make a use of its members warn or fail.
    /// </summary>
    private static bool IsInDeprecatedType(INamedTypeSymbol? type)
    {
        for (; type is not null; type = type.ContainingType)
        {
            foreach (var attr in type.GetAttributes())
            {
                var name = attr.AttributeClass?.ToDisplayString();
                if (string.Equals(name, "System.ObsoleteAttribute", System.StringComparison.Ordinal)
                    || string.Equals(name, "System.Diagnostics.CodeAnalysis.ExperimentalAttribute", System.StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether the property, or its getter's return value, carries an attribute from
    /// <c>System.Diagnostics.CodeAnalysis</c>, such as <c>[MaybeNull]</c>, that changes the
    /// null state of the value the validator reads from it.
    /// </summary>
    private static bool HasNullabilityAttribute(IPropertySymbol property) =>
        HasCodeAnalysisAttribute(property.GetAttributes())
        || (property.GetMethod is { } getter
            && (HasCodeAnalysisAttribute(getter.GetAttributes()) || HasCodeAnalysisAttribute(getter.GetReturnTypeAttributes())));

    private static bool HasCodeAnalysisAttribute(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            if (string.Equals(attr.AttributeClass?.ContainingNamespace?.ToDisplayString(), "System.Diagnostics.CodeAnalysis", System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

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
