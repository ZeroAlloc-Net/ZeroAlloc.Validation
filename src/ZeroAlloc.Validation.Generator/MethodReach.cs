namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Whether the generated validator can call a model method as <c>instance.Method(...)</c>.
/// </summary>
internal enum MethodReach
{
    /// <summary>The call binds to an accessible instance method.</summary>
    Callable,

    /// <summary>
    /// The call the validator would contain does not compile, for a reason other than a static
    /// or inaccessible method: no member of that name takes the arguments, the call is
    /// ambiguous, or its result cannot be used as a condition. ZV0030.
    /// </summary>
    NotFound,

    /// <summary>
    /// The generator's input compilation has no member of that name for the call. Another
    /// source generator may add one, which only the final compilation contains, so the call is
    /// emitted and the final compilation decides. Not reported.
    /// </summary>
    MissingFromInput,

    /// <summary>The method is static, so <c>instance.Method()</c> is CS0176. ZV0028.</summary>
    Static,

    /// <summary>The method is declared on the model and the validator cannot access it. ZV0028.</summary>
    Inaccessible,

    /// <summary>The method is declared on a base type and the validator cannot access it. ZV0017.</summary>
    InaccessibleOnBase,
}
