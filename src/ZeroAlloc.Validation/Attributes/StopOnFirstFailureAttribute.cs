namespace ZeroAlloc.Validation;

/// <summary>
/// Stops evaluating a property's rules at the first one that fails, instead of reporting every
/// rule the value violates.
/// </summary>
/// <remarks>
/// On a property it applies to that property. On a class it applies to every property the class
/// declares, which is the usual intent when combined with
/// <c>[Validate(StopOnFirstFailure = true)]</c>: the model then reports the first failing rule of
/// the first failing property, and nothing else.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property, AllowMultiple = false)]
public sealed class StopOnFirstFailureAttribute : Attribute { }
