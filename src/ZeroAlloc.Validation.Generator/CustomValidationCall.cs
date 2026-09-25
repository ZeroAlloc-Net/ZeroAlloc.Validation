namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// A <c>[CustomValidation]</c> method the generated validator calls.
/// </summary>
/// <param name="Name">The method's name.</param>
/// <param name="ByRef">Whether its result is a span walked by reference.</param>
/// <param name="Receiver">
/// The fully qualified type to call it through, or <see langword="null"/> to call it on the model
/// directly; see <c>RuleEmitter.CustomValidationReceiver</c>.
/// </param>
internal readonly record struct CustomValidationCall(string Name, bool ByRef, string? Receiver);
