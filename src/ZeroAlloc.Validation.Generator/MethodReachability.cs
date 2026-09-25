using Microsoft.CodeAnalysis;
using System.Collections.Generic;

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
    /// <c>When</c>/<c>Unless</c> call. Overloads that cannot take those arguments are ignored, and
    /// an accessible method hides a base method with the same signature. The call is
    /// <see cref="MethodReach.Callable"/> when an accessible instance overload applies; otherwise
    /// the applicable overloads say why not, static first, then inaccessible on the model, then
    /// inaccessible on a base type. <paramref name="method"/> is the overload the verdict is about.
    /// </summary>
    public static MethodReach Resolve(
        Compilation compilation,
        INamedTypeSymbol model,
        string name,
        ITypeSymbol? argumentType,
        out IMethodSymbol? method)
    {
        IMethodSymbol? isStatic = null;
        IMethodSymbol? inaccessibleOwn = null;
        IMethodSymbol? inaccessibleBase = null;
        var hidden = new HashSet<string>(StringComparer.Ordinal);

        for (var type = model; type is not null; type = type.BaseType)
        {
            var visible = new List<string>();
            foreach (var member in type.GetMembers(name))
            {
                if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary } candidate) continue;
                if (!IsApplicable(compilation, candidate, argumentType)) continue;

                var key = SignatureKey(candidate);
                if (hidden.Contains(key)) continue;

                if (IsAccessible(compilation, candidate))
                {
                    if (!candidate.IsStatic)
                    {
                        method = candidate;
                        return MethodReach.Callable;
                    }
                    // An accessible static method still hides the base methods it matches.
                    visible.Add(key);
                    isStatic ??= candidate;
                }
                else if (candidate.IsStatic)
                {
                    isStatic ??= candidate;
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

            for (int i = 0; i < visible.Count; i++)
                hidden.Add(visible[i]);
        }

        if (isStatic is not null)
        {
            method = isStatic;
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
    /// Whether a method declared on <paramref name="declaringType"/> is reported by the generation
    /// of a <c>[Validate]</c> base type of <paramref name="model"/> in this compilation rather than
    /// by <paramref name="model"/>'s own. That base type's validator runs the same checks from the
    /// same assembly, so each usage is reported once, by the validator it belongs to.
    /// </summary>
    public static bool IsReportedByBaseValidator(Compilation compilation, INamedTypeSymbol model, INamedTypeSymbol? declaringType)
    {
        if (declaringType is null) return false;

        bool pastValidateBase = false;
        for (var type = model.BaseType; type is not null; type = type.BaseType)
        {
            if (!pastValidateBase
                && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)
                && HasValidateAttribute(type))
            {
                pastValidateBase = true;
            }

            if (pastValidateBase && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, declaringType.OriginalDefinition))
                return true;
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

    private static string SignatureKey(IMethodSymbol method)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Ordinal > 0) sb.Append(',');
            sb.Append((int)parameter.RefKind).Append(' ').Append(parameter.Type.ToDisplayString());
        }
        return sb.ToString();
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
