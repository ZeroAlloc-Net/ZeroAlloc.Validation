namespace ZeroAlloc.Validation;

/// <summary>
/// A validator seen without its model type: the model type it validates, and validation of an
/// instance passed as <see cref="object"/>. Every <see cref="ValidatorFor{T}"/> is one.
/// </summary>
/// <remarks>
/// A closing of a generic <c>[Validate]</c> model, such as <c>Page&lt;Order&gt;</c>, cannot be
/// dispatched to by a <c>case</c> on its model type in generated code, because the closings are
/// not all known there. The generated registrations therefore also list each closing's validator
/// as an <see cref="IModelValidator"/>, so it can be looked up by the runtime type of the value to
/// validate, without reflection or <c>MakeGenericType</c>.
/// </remarks>
public interface IModelValidator
{
    /// <summary>The type of the model this validator validates.</summary>
    Type ModelType { get; }

    /// <summary>
    /// Validates <paramref name="instance"/>, which must be an instance of <see cref="ModelType"/>.
    /// </summary>
    /// <param name="instance">The model to validate.</param>
    /// <param name="ct">The token that cancels the validation's asynchronous rules.</param>
    /// <returns>The result of the validation.</returns>
    /// <exception cref="InvalidCastException"><paramref name="instance"/> is not a <see cref="ModelType"/>.</exception>
    ValueTask<ValidationResult> ValidateAsync(object instance, CancellationToken ct);
}
