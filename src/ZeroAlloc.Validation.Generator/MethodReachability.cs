using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Reachability of the methods a generated validator calls on the model. The validator is a
/// separate class in the model's assembly, so it can call only instance methods that are
/// accessible from that assembly, meaning <c>public</c>, <c>internal</c> or
/// <c>protected internal</c>. Anything else would be emitted as a call that fails with CS0122
/// or CS0176, so the rule is skipped and reported instead. Calls a rule makes by name are
/// resolved by the compiler itself, through <see cref="MethodCallProbe"/>.
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
    /// Whether a usage declared on <paramref name="declaringType"/> is reported by the generation
    /// of a <c>[Validate]</c> base type of <paramref name="model"/> in this compilation rather than
    /// by <paramref name="model"/>'s own; see <see cref="FindReportingBaseValidator"/>.
    /// A generic <c>[Validate]</c> base type gets no validator, ZV0029, so it reports nothing and
    /// leaves its usages to <paramref name="model"/>, issue #219.
    /// </summary>
    public static bool IsReportedByBaseValidator(Compilation compilation, INamedTypeSymbol model, INamedTypeSymbol? declaringType) =>
        FindReportingBaseValidator(compilation, model, declaringType) is not null;

    /// <summary>
    /// The <c>[Validate]</c> base type of <paramref name="model"/> in this compilation whose
    /// generation reports a usage declared on <paramref name="declaringType"/>, or
    /// <see langword="null"/> when <paramref name="model"/> reports it. A base type reports it only
    /// when it walks the declaring type: it is the declaring type itself, or it sits below the
    /// declaring type and includes base properties. A base type with
    /// <c>IncludeBaseProperties = false</c> does not see the types above it, so their usages stay
    /// <paramref name="model"/>'s to report. That base type's validator runs the same checks from
    /// the same assembly, so each usage is reported once, by one validator: the declaring type's
    /// own when it is <c>[Validate]</c>, and otherwise that of the walking base type nearest to it.
    /// </summary>
    public static INamedTypeSymbol? FindReportingBaseValidator(Compilation compilation, INamedTypeSymbol model, INamedTypeSymbol? declaringType)
    {
        if (declaringType is null) return null;

        INamedTypeSymbol? walking = null;
        for (var type = model.BaseType; type is not null; type = type.BaseType)
        {
            bool isValidated = SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)
                && HasValidateAttribute(type)
                && GeneratedValidatorReach.HasGeneratedValidator(type, compilation);

            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, declaringType.OriginalDefinition))
                return isValidated ? type : walking;

            if (isValidated && MemberWalker.IncludesBaseProperties(type))
                walking = type;
        }
        return null;
    }

    /// <summary>
    /// Whether the generated validator, a top-level class in the compilation's assembly, may
    /// call <paramref name="method"/>. A method whose containing type is itself out of reach is
    /// not blamed: the model is then out of reach too, which is not a problem with the method.
    /// </summary>
    private static bool IsAccessible(Compilation compilation, ISymbol member) =>
        compilation.IsSymbolAccessibleWithin(member, compilation.Assembly)
        || !compilation.IsSymbolAccessibleWithin(member.ContainingType, compilation.Assembly);

    private static bool IsDeclaredOn(IMethodSymbol method, INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, type.OriginalDefinition);

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
