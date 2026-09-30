using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Inject;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// A generic <c>[Validate]</c> model, or one declared inside a generic type, as the Inject and
/// Options generators see it, issue #238: what they need to emit a method generic over its type
/// parameters, the <c>Add…Validator&lt;…&gt;()</c> registration helper and the generic
/// <c>ValidateWithZeroAlloc&lt;…&gt;()</c> overload, which close them at the call site. Strings,
/// flags and registrations only, so the value compares equal across compilations while nothing it
/// was read from changes, like <see cref="ValidatedModelInfo"/>, issue #209.
/// </summary>
internal sealed class GenericModelInfo : IEquatable<GenericModelInfo>
{
    private GenericModelInfo(
        string? namespaceName,
        string? hintNamespace,
        string validatorName,
        string name,
        string fullyQualifiedName,
        EquatableArray<string> typeParameterNames,
        string typeParameterList,
        EquatableArray<string> constraintClauses,
        bool isEffectivelyPublic,
        RegistrationNames names,
        EquatableArray<RegistrationNode> registrations)
    {
        NamespaceName = namespaceName;
        HintNamespace = hintNamespace;
        ValidatorName = validatorName;
        Name = name;
        FullyQualifiedName = fullyQualifiedName;
        TypeParameterNames = typeParameterNames;
        TypeParameterList = typeParameterList;
        ConstraintClauses = constraintClauses;
        IsEffectivelyPublic = isEffectivelyPublic;
        Names = names;
        Registrations = registrations;
    }

    /// <summary>The model's namespace as C# code writes it, or <see langword="null"/> for the global namespace.</summary>
    public string? NamespaceName { get; }

    /// <summary>The model's namespace as a hint name writes it, unescaped, or <see langword="null"/> for the global namespace.</summary>
    public string? HintNamespace { get; }

    /// <summary>The simple name of the model's generated validator, such as <c>PageValidator</c>.</summary>
    public string ValidatorName { get; }

    /// <summary>The model's simple name, for documentation.</summary>
    public string Name { get; }

    /// <summary>The model as generated code writes it, over its type parameters: <c>global::Ns.Page&lt;TItem&gt;</c>.</summary>
    public string FullyQualifiedName { get; }

    /// <summary>
    /// The declared names of the type parameters of the model and of every type containing it,
    /// outermost first, unescaped, for the <c>&lt;typeparam&gt;</c> tags.
    /// </summary>
    public EquatableArray<string> TypeParameterNames { get; }

    /// <summary>The type parameter list the method declares, <c>&lt;TItem&gt;</c>, keywords escaped.</summary>
    public string TypeParameterList { get; }

    /// <summary>The method's <c>where</c> clauses, the model's constraints, one per constrained type parameter.</summary>
    public EquatableArray<string> ConstraintClauses { get; }

    /// <summary>Whether the model and every type containing it are public.</summary>
    public bool IsEffectivelyPublic { get; }

    /// <summary>The identifiers the method declares, free of its type parameters' names.</summary>
    public RegistrationNames Names { get; }

    /// <summary>
    /// The registrations of the model's validator and of every validator it takes, transitively,
    /// over the model's type parameters; see <see cref="ValidatorRegistrationEmitter.OpenRegistrationGraph"/>.
    /// </summary>
    public EquatableArray<RegistrationNode> Registrations { get; }

    /// <summary>
    /// The name of the registration helper method, <c>Add</c> and the validator's name, such as
    /// <c>AddPageValidator</c> or <c>AddEnvelope_HeaderValidator</c>. Validator names are unique per
    /// namespace and arity, ZV0031, so helper names are too, and helpers of the same name that
    /// differ in arity are overloads.
    /// </summary>
    public string HelperName => "Add" + ValidatorName;

    /// <summary>Extracts the data of <paramref name="model"/>, a generic model as declared, in <paramref name="compilation"/>.</summary>
    public static GenericModelInfo From(INamedTypeSymbol model, Compilation compilation)
    {
        model = model.OriginalDefinition;
        var parameters = GenericSignature.TypeParameters(model);
        var parameterNames = new List<string>(parameters.Count);
        for (var i = 0; i < parameters.Count; i++)
            parameterNames.Add(parameters[i].Name);

        var names = RegistrationNames.Avoiding(parameterNames);
        return new GenericModelInfo(
            GeneratedValidatorNames.NamespaceName(model),
            GeneratedValidatorNames.HintNamespaceName(model),
            GeneratedValidatorNames.ValidatorName(model),
            model.Name,
            model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EquatableArray.From(parameterNames),
            GenericSignature.ParameterList(parameters),
            EquatableArray.From(GenericSignature.ConstraintClauses(parameters)),
            ValidatedModelInfo.IsPublicWithContainers(model),
            names,
            ValidatorRegistrationEmitter.OpenRegistrationGraph(model, compilation, names));
    }

    /// <summary>The collected generic models, as an equatable array, dropping the <see langword="null"/> marks of every other model.</summary>
    public static EquatableArray<GenericModelInfo> Collected(ImmutableArray<GenericModelInfo?> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
            return EquatableArray<GenericModelInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<GenericModelInfo>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (candidate is not null)
                builder.Add(candidate);
        }
        return new EquatableArray<GenericModelInfo>(builder.ToImmutable());
    }

    public bool Equals(GenericModelInfo? other) =>
        other is not null
        && string.Equals(NamespaceName, other.NamespaceName, StringComparison.Ordinal)
        && string.Equals(HintNamespace, other.HintNamespace, StringComparison.Ordinal)
        && string.Equals(ValidatorName, other.ValidatorName, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(FullyQualifiedName, other.FullyQualifiedName, StringComparison.Ordinal)
        && TypeParameterNames.Equals(other.TypeParameterNames)
        && string.Equals(TypeParameterList, other.TypeParameterList, StringComparison.Ordinal)
        && ConstraintClauses.Equals(other.ConstraintClauses)
        && IsEffectivelyPublic == other.IsEffectivelyPublic
        && Names.Equals(other.Names)
        && Registrations.Equals(other.Registrations);

    public override bool Equals(object? obj) => Equals(obj as GenericModelInfo);

    public override int GetHashCode() =>
        unchecked((StringComparer.Ordinal.GetHashCode(FullyQualifiedName) * 31) + Registrations.GetHashCode());
}
