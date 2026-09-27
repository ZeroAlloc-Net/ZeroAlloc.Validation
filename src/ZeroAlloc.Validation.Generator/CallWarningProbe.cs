namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// How <see cref="MethodCallProbe.CallWarnings"/> finds the compiler warnings on a model's
/// generated calls: <see cref="RuleEmitter.CallWarningProbeFor"/>.
/// </summary>
internal enum CallWarningProbe
{
    /// <summary>No call can warn; nothing is compiled.</summary>
    None,

    /// <summary>
    /// Only calls that take no argument can warn: <c>When</c> and <c>Unless</c> guards,
    /// <c>[SkipWhen]</c> and <c>[CustomValidation]</c>. The code before such a call cannot change
    /// what it warns, so its warnings come from the per-call probe that already decides whether
    /// it compiles, and the model's <c>Validate</c> body is not compiled, issue #256.
    /// </summary>
    Calls,

    /// <summary>
    /// A call whose warnings may depend on the code before it: a <c>[Must]</c> predicate or
    /// custom rule given the property's value, or a property read that may warn. The model's
    /// whole <c>Validate</c> body is compiled. So is it for a <c>[CustomValidation]</c> call made
    /// through a cast, which the per-call probe does not compile as written.
    /// </summary>
    Body,
}
