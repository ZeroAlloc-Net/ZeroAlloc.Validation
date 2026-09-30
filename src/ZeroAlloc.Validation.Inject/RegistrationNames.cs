using System;
using System.Collections.Generic;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// The identifiers the generated registration code declares: the <c>IServiceCollection</c> the
/// lines register into, the <c>IServiceProvider</c> parameter of a registry entry's lambda, and
/// the <c>OptionsBuilder</c> parameter of an options overload. A method generic over a model's
/// type parameters, issue #238, cannot declare a parameter, local or lambda parameter with the
/// name of one of its type parameters, CS0412, so for a model with a type parameter named
/// <c>services</c>, <c>sp</c> or <c>builder</c> that identifier gets the first free numeric suffix.
/// Every other method keeps the names it always had.
/// </summary>
internal sealed class RegistrationNames : IEquatable<RegistrationNames>
{
    public static readonly RegistrationNames Default = new("services", "sp", "builder");

    private RegistrationNames(string services, string provider, string builder)
    {
        Services = services;
        Provider = provider;
        Builder = builder;
    }

    /// <summary>The service collection the lines register into, <c>services</c>.</summary>
    public string Services { get; }

    /// <summary>The service provider parameter of a registry entry's lambda, <c>sp</c>.</summary>
    public string Provider { get; }

    /// <summary>The options builder parameter of an options overload, <c>builder</c>.</summary>
    public string Builder { get; }

    /// <summary>
    /// The names for a method declaring type parameters named <paramref name="typeParameters"/>:
    /// <see cref="Default"/>'s, each with the first numeric suffix that makes it free.
    /// </summary>
    public static RegistrationNames Avoiding(IReadOnlyCollection<string> typeParameters)
    {
        var taken = new HashSet<string>(typeParameters, StringComparer.Ordinal);
        return new RegistrationNames(
            Free(Default.Services, taken),
            Free(Default.Provider, taken),
            Free(Default.Builder, taken));
    }

    private static string Free(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name))
            return name;

        for (var i = 2; ; i++)
        {
            var candidate = name + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    public bool Equals(RegistrationNames? other) =>
        other is not null
        && string.Equals(Services, other.Services, StringComparison.Ordinal)
        && string.Equals(Provider, other.Provider, StringComparison.Ordinal)
        && string.Equals(Builder, other.Builder, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as RegistrationNames);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Services);
}
