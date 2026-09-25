namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Whether the generated validator can call a model method as <c>instance.Method(...)</c>.
/// </summary>
internal enum MethodReach
{
    /// <summary>The call binds to an accessible instance method.</summary>
    Callable,

    /// <summary>
    /// No method of that name takes the arguments the validator passes. Left to the compiler,
    /// which already reports a <c>nameof</c> that does not resolve.
    /// </summary>
    NotFound,

    /// <summary>The method is static, so <c>instance.Method()</c> is CS0176. ZV0028.</summary>
    Static,

    /// <summary>The method is declared on the model and the validator cannot access it. ZV0028.</summary>
    Inaccessible,

    /// <summary>The method is declared on a base type and the validator cannot access it. ZV0017.</summary>
    InaccessibleOnBase,
}
