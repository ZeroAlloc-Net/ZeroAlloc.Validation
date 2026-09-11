namespace ZeroAlloc.Validation;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class ValidateAttribute : Attribute
{
    public bool StopOnFirstFailure { get; set; }

    /// <summary>
    /// When <see langword="true"/> (the default), rules declared on base types are emitted into
    /// this type's generated validator, base-most first. Set to <see langword="false"/> to validate
    /// only the members declared directly on this type.
    /// </summary>
    public bool IncludeBaseProperties { get; set; } = true;
}
