using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Resolves the methods a generated validator calls on the model: <c>[CustomValidation]</c>
/// methods, <c>[Must]</c> predicates and <c>When</c>/<c>Unless</c> conditions. The validator is a
/// separate class in the model's assembly, so it can call only instance methods that are
/// accessible from that assembly, meaning <c>public</c>, <c>internal</c> or
/// <c>protected internal</c>. Anything else would be emitted as a call that fails with CS0122
/// or CS0176, so the rule is skipped and reported instead.
/// </summary>
internal static class MethodReachability
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

    /// <summary>
    /// Classifies a <c>[CustomValidation]</c> method found on <paramref name="model"/> or one of
    /// its base types. A static method is reported as static even when it is also inaccessible,
    /// because widening it would not make it callable.
    /// </summary>
    public static MethodReach Classify(Compilation compilation, INamedTypeSymbol model, IMethodSymbol method)
    {
        if (method.IsStatic) return MethodReach.Static;
        if (IsAccessible(compilation, method)) return MethodReach.Callable;
        return IsDeclaredOn(method, model) ? MethodReach.Inaccessible : MethodReach.InaccessibleOnBase;
    }

    /// <summary>
    /// Resolves the method a rule names, the way <c>instance.Name(args)</c> binds from the
    /// generated validator. <paramref name="argumentType"/> is the type of the single argument a
    /// <c>[Must]</c> predicate receives, or <see langword="null"/> for a parameterless
    /// <c>When</c>/<c>Unless</c> call. Overloads that cannot take those arguments are ignored.
    /// Like C# member lookup, the walk stops at the most-derived type that declares an accessible
    /// applicable overload: the call is <see cref="MethodReach.Callable"/> when one of that type's
    /// overloads is an instance method, and <see cref="MethodReach.Static"/> when they are all
    /// static, since the call then binds to a static method whatever the base types declare.
    /// When no type declares an accessible overload, the inaccessible ones say why, static
    /// first, then inaccessible on the model, then inaccessible on a base type.
    /// <paramref name="method"/> is the overload the verdict is about.
    /// </summary>
    public static MethodReach Resolve(
        Compilation compilation,
        INamedTypeSymbol model,
        string name,
        ITypeSymbol? argumentType,
        out IMethodSymbol? method)
    {
        IMethodSymbol? inaccessibleStatic = null;
        IMethodSymbol? inaccessibleOwn = null;
        IMethodSymbol? inaccessibleBase = null;

        for (var type = model; type is not null; type = type.BaseType)
        {
            IMethodSymbol? accessibleStatic = null;
            foreach (var member in type.GetMembers(name))
            {
                if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary } candidate) continue;
                if (!IsApplicable(compilation, candidate, argumentType)) continue;

                if (IsAccessible(compilation, candidate))
                {
                    if (!candidate.IsStatic)
                    {
                        method = candidate;
                        return MethodReach.Callable;
                    }
                    accessibleStatic ??= candidate;
                }
                else if (candidate.IsStatic)
                {
                    inaccessibleStatic ??= candidate;
                }
                else if (IsDeclaredOn(candidate, model))
                {
                    inaccessibleOwn ??= candidate;
                }
                else
                {
                    inaccessibleBase ??= candidate;
                }
            }

            // This type's accessible overloads are all static; base methods are not considered.
            if (accessibleStatic is not null)
            {
                method = accessibleStatic;
                return MethodReach.Static;
            }
        }

        if (inaccessibleStatic is not null)
        {
            method = inaccessibleStatic;
            return MethodReach.Static;
        }
        if (inaccessibleOwn is not null)
        {
            method = inaccessibleOwn;
            return MethodReach.Inaccessible;
        }
        method = inaccessibleBase;
        return inaccessibleBase is not null ? MethodReach.InaccessibleOnBase : MethodReach.NotFound;
    }

    /// <summary>
    /// Whether a usage declared on <paramref name="declaringType"/> is reported by the generation
    /// of a <c>[Validate]</c> base type of <paramref name="model"/> in this compilation rather than
    /// by <paramref name="model"/>'s own. That holds only when such a base type walks the
    /// declaring type: it is the declaring type itself, or it sits below the declaring type and
    /// includes base properties. A base type with <c>IncludeBaseProperties = false</c> does not
    /// see the types above it, so their usages stay <paramref name="model"/>'s to report. That base
    /// type's validator runs the same checks from the same assembly, so each usage is reported
    /// once, by one validator. That holds for base types that have a validator. A generic
    /// <c>[Validate]</c> base type gets none, ZV0029, so it reports nothing and leaves its usages
    /// to <paramref name="model"/>, issue #219: its members are then reported by each model that
    /// derives from it, unless a non-generic <c>[Validate]</c> type in between reports them.
    /// </summary>
    public static bool IsReportedByBaseValidator(Compilation compilation, INamedTypeSymbol model, INamedTypeSymbol? declaringType)
    {
        if (declaringType is null) return false;

        bool coveredFromBelow = false;
        for (var type = model.BaseType; type is not null; type = type.BaseType)
        {
            bool isValidated = SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)
                && HasValidateAttribute(type)
                && GeneratedValidatorReach.HasGeneratedValidator(type, compilation);

            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, declaringType.OriginalDefinition))
                return coveredFromBelow || isValidated;

            if (isValidated && MemberWalker.IncludesBaseProperties(type))
                coveredFromBelow = true;
        }
        return false;
    }

    /// <summary>
    /// Whether the generated validator, a top-level class in the compilation's assembly, may
    /// call <paramref name="method"/>. A method whose containing type is itself out of reach is
    /// not blamed: the model is then out of reach too, which is not a problem with the method.
    /// </summary>
    private static bool IsAccessible(Compilation compilation, IMethodSymbol method) =>
        compilation.IsSymbolAccessibleWithin(method, compilation.Assembly)
        || !compilation.IsSymbolAccessibleWithin(method.ContainingType, compilation.Assembly);

    private static bool IsDeclaredOn(IMethodSymbol method, INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, type.OriginalDefinition);

    /// <summary>
    /// Whether <paramref name="method"/> can take the call's arguments: one argument of
    /// <paramref name="argumentType"/>, or none when it is <see langword="null"/>. Parameters
    /// beyond those must be optional or <c>params</c>.
    /// </summary>
    private static bool IsApplicable(Compilation compilation, IMethodSymbol method, ITypeSymbol? argumentType)
    {
        var parameters = method.Parameters;
        int passed = argumentType is null ? 0 : 1;
        if (parameters.Length < passed) return false;

        foreach (var parameter in parameters)
        {
            if (parameter.Ordinal >= passed && !parameter.IsOptional && !parameter.IsParams)
                return false;
        }

        if (argumentType is null) return true;

        var first = parameters[0];
        if (first.RefKind is not (RefKind.None or RefKind.In)) return false;
        if (method.IsGenericMethod || first.IsParams) return true;
        return compilation.ClassifyCommonConversion(argumentType, first.Type).IsImplicit;
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
}
