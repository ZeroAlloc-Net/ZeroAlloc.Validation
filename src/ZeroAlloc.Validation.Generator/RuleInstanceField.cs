namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// One user-defined rule instance a generated validator declares as a static field.
/// </summary>
/// <param name="TypeName">The fully qualified attribute type.</param>
/// <param name="Initializer">The object creation that rebuilds the usage.</param>
/// <param name="IsObsolete">
/// Whether the initializer names an <c>[Obsolete]</c> symbol, so the declaration is wrapped in a
/// pragma: the compiler already warns at the usage in user code.
/// </param>
internal readonly record struct RuleInstanceField(string TypeName, string Initializer, bool IsObsolete);
