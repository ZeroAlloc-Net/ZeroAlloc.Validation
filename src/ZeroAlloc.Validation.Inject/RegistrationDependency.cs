using System;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// A validator a generated constructor takes: <c>ValidatorFor</c> of a nested <c>[Validate]</c>
/// model, or a <c>[ValidateWith]</c> validator taken as itself.
/// </summary>
internal sealed class RegistrationDependency : IEquatable<RegistrationDependency>
{
    public RegistrationDependency(bool isValidateWith, string key, string? validateWithRegistration, string? followedModelKey)
    {
        IsValidateWith = isValidateWith;
        Key = key;
        ValidateWithRegistration = validateWithRegistration;
        FollowedModelKey = followedModelKey;
    }

    /// <summary>Whether this is a <c>[ValidateWith]</c> validator rather than a nested model.</summary>
    public bool IsValidateWith { get; }

    /// <summary>The nested model's <see cref="RegistrationNode.Key"/>, or the <c>[ValidateWith]</c> validator's.</summary>
    public string Key { get; }

    /// <summary>
    /// For a <c>[ValidateWith]</c> validator, the <c>TryAddSingleton</c> line registering it, or
    /// <see langword="null"/> when the container could not construct it.
    /// </summary>
    public string? ValidateWithRegistration { get; }

    /// <summary>
    /// For a <c>[ValidateWith]</c> validator that is a referenced assembly's generated validator of
    /// the property's own model, that model's key: its validator's dependencies are followed too.
    /// </summary>
    public string? FollowedModelKey { get; }

    public bool Equals(RegistrationDependency? other) =>
        other is not null
        && IsValidateWith == other.IsValidateWith
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(ValidateWithRegistration, other.ValidateWithRegistration, StringComparison.Ordinal)
        && string.Equals(FollowedModelKey, other.FollowedModelKey, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as RegistrationDependency);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
}
