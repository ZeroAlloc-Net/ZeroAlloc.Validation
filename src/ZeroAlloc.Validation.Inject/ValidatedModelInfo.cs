using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Inject;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// A <c>[Validate]</c> model with a generated validator, as the Inject, Options and ASP.NET Core
/// generators see it: strings, flags and its validator registrations, all extracted in the
/// syntax provider's transform. No symbol or compilation is kept, so the value compares equal
/// across compilations while nothing it was read from changed, and the generator's output step
/// stays cached, issue #209.
/// </summary>
internal sealed class ValidatedModelInfo : IEquatable<ValidatedModelInfo>
{
    public ValidatedModelInfo(
        string fullyQualifiedName,
        string name,
        bool isEffectivelyPublic,
        EquatableArray<RegistrationNode> registrations)
    {
        FullyQualifiedName = fullyQualifiedName;
        Name = name;
        IsEffectivelyPublic = isEffectivelyPublic;
        Registrations = registrations;
    }

    /// <summary>The model's name as generated code writes it, with the <c>global::</c> prefix.</summary>
    public string FullyQualifiedName { get; }

    /// <summary>The model's simple name.</summary>
    public string Name { get; }

    /// <summary>Whether the model and every type containing it are public.</summary>
    public bool IsEffectivelyPublic { get; }

    /// <summary>
    /// The model's own registration first, then every registration its validator's constructor
    /// dependencies need, transitively; see <see cref="ValidatorRegistrationEmitter"/>.
    /// </summary>
    public EquatableArray<RegistrationNode> Registrations { get; }

    /// <summary>Extracts the model's data from <paramref name="model"/> in <paramref name="compilation"/>.</summary>
    public static ValidatedModelInfo From(INamedTypeSymbol model, Compilation compilation) =>
        new(
            model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            model.Name,
            IsPublicWithContainers(model),
            ValidatorRegistrationEmitter.RegistrationGraph(model, compilation));

    /// <summary>
    /// The collected models that have a generated validator, as an equatable array, so the step
    /// after Collect compares by value. A companion generator's transform marks every other one as
    /// <see langword="null"/>.
    /// </summary>
    public static EquatableArray<ValidatedModelInfo> WithGeneratedValidator(ImmutableArray<ValidatedModelInfo?> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
            return EquatableArray<ValidatedModelInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<ValidatedModelInfo>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (candidate is not null)
                builder.Add(candidate);
        }
        return new EquatableArray<ValidatedModelInfo>(builder.ToImmutable());
    }

    /// <summary>Whether <paramref name="type"/> and every type containing it are public.</summary>
    public static bool IsPublicWithContainers(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility != Accessibility.Public)
                return false;
        }
        return true;
    }

    public bool Equals(ValidatedModelInfo? other) =>
        other is not null
        && string.Equals(FullyQualifiedName, other.FullyQualifiedName, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && IsEffectivelyPublic == other.IsEffectivelyPublic
        && Registrations.Equals(other.Registrations);

    public override bool Equals(object? obj) => Equals(obj as ValidatedModelInfo);

    public override int GetHashCode() =>
        unchecked((StringComparer.Ordinal.GetHashCode(FullyQualifiedName) * 31) + Registrations.GetHashCode());
}
