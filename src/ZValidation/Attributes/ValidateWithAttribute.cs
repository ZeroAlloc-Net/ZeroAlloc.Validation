namespace ZValidation;

/// <summary>
/// Specifies an explicit validator type for a property whose type does not carry [Validate].
/// Use for third-party or framework types you do not control.
/// </summary>
/// <param name="validatorType">
/// The validator type to use. Must implement <see cref="ValidatorFor{T}"/> where T matches
/// the property type (or element type for collection properties).
/// </param>
[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
public sealed class ValidateWithAttribute : System.Attribute
{
    public ValidateWithAttribute(System.Type validatorType) => ValidatorType = validatorType;
    public System.Type ValidatorType { get; }
}
