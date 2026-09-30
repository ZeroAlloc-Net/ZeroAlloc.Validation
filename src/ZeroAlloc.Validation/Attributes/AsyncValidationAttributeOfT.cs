namespace ZeroAlloc.Validation;

/// <summary>
/// Base class for a reusable, user-defined property rule that checks its value asynchronously,
/// such as a uniqueness check or a remote lookup. The source generator rebuilds each usage once as
/// a static instance of the derived attribute and awaits <see cref="IsValidAsync"/> with the
/// property value. No reflection is involved.
/// </summary>
/// <remarks>
/// <para>
/// A model with at least one asynchronous rule, directly or through a nested or collection
/// property, gets a generated <see cref="ValidatorFor{T}.ValidateAsync"/> that runs every rule in
/// declaration order, awaiting the asynchronous ones. <c>Message</c>, <see cref="RuleMessageAttribute"/>,
/// <c>ErrorCode</c>, <c>Severity</c>, <c>When</c>, <c>Unless</c> and stop-on-first-failure behave
/// as they do for <see cref="ValidationAttribute{T}"/>. The synchronous
/// <see cref="ValidatorFor{T}.Validate"/> of such a model cannot run the rule, so it throws
/// <see cref="System.NotSupportedException"/> instead of skipping it.
/// </para>
/// <para>
/// One instance serves every validation, on every thread, so <see cref="IsValidAsync"/> must be
/// stateless and thread-safe. The rule takes no services; it reaches what it needs through static
/// state it owns.
/// </para>
/// </remarks>
/// <typeparam name="T">The value type the rule checks. The property type must convert to it implicitly.</typeparam>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class AsyncValidationAttribute<T> : ValidationAttribute
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> passes the rule.
    /// </summary>
    /// <param name="value">The property value.</param>
    /// <param name="ct">The token passed to <see cref="ValidatorFor{T}.ValidateAsync"/>.</param>
    public abstract ValueTask<bool> IsValidAsync(T value, CancellationToken ct);
}
