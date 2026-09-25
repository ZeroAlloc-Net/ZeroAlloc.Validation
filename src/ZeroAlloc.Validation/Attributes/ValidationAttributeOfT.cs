namespace ZeroAlloc.Validation;

/// <summary>
/// Base class for a reusable, user-defined property rule. The source generator rebuilds each
/// usage once as a static instance of the derived attribute and calls <see cref="IsValid"/>
/// with the property value. No reflection is involved.
/// </summary>
/// <remarks>
/// <para>
/// A validation that passes allocates nothing for the rule, with two exceptions. A value-type
/// property checked by a rule whose <typeparamref name="T"/> is a reference type, such as
/// <see cref="object"/> or an interface, is boxed on every call, and a user-defined implicit conversion to
/// <typeparamref name="T"/> runs on every call. Declare <typeparamref name="T"/> as the
/// property's own type to avoid both. A validation that fails allocates its result, the same
/// as a built-in rule.
/// </para>
/// <para>
/// One instance serves every validation, on every thread, so <see cref="IsValid"/> must be
/// stateless and thread-safe.
/// </para>
/// </remarks>
/// <typeparam name="T">The value type the rule checks. The property type must convert to it implicitly.</typeparam>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class ValidationAttribute<T> : ValidationAttribute
{
    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> passes the rule.</summary>
    public abstract bool IsValid(T value);
}
