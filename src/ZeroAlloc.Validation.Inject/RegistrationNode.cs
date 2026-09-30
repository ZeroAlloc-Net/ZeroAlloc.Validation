using System;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// One type in a model's registration graph: a <c>[Validate]</c> model whose generated validator
/// may be registered as <c>ValidatorFor</c> of it, and the validators that validator's constructor
/// takes.
/// </summary>
internal sealed class RegistrationNode : IEquatable<RegistrationNode>
{
    public RegistrationNode(string key, string? registration, EquatableArray<RegistrationDependency> dependencies)
        : this(key, registration, registryEntry: null, dependencies)
    {
    }

    public RegistrationNode(string key, string? registration, string? registryEntry, EquatableArray<RegistrationDependency> dependencies)
    {
        Key = key;
        Registration = registration;
        RegistryEntry = registryEntry;
        Dependencies = dependencies;
    }

    /// <summary>Identifies the model across the graphs of every model: its assembly and name.</summary>
    public string Key { get; }

    /// <summary>
    /// The <c>TryAddSingleton</c> line registering the model's validator, or <see langword="null"/>
    /// when this compilation cannot name that validator, which is then left to the assembly
    /// declaring it.
    /// </summary>
    public string? Registration { get; }

    /// <summary>
    /// For a closing of a generic model, the <c>TryAddEnumerable</c> line that also lists its
    /// validator as an <c>IModelValidator</c>, the registry a closing is looked up in by its
    /// runtime type, issue #238; otherwise <see langword="null"/>, so a non-generic model's
    /// registrations stay exactly as they were.
    /// </summary>
    public string? RegistryEntry { get; }

    /// <summary>
    /// The validators the model's generated constructor takes, in order. Empty when
    /// <see cref="Registration"/> is <see langword="null"/>, since the model is then not followed.
    /// </summary>
    public EquatableArray<RegistrationDependency> Dependencies { get; }

    public bool Equals(RegistrationNode? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(Registration, other.Registration, StringComparison.Ordinal)
        && string.Equals(RegistryEntry, other.RegistryEntry, StringComparison.Ordinal)
        && Dependencies.Equals(other.Dependencies);

    public override bool Equals(object? obj) => Equals(obj as RegistrationNode);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
}
