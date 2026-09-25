using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// A rule's call to a model method, with how it resolves; see
/// <see cref="RuleEmitter.GetUnreachableMethodCalls"/> and <see cref="RuleEmitter.ResolveSkipWhen"/>.
/// </summary>
internal readonly record struct ResolvedMethodCall(
    AttributeData Attribute,
    string Usage,
    string MethodName,
    MethodResolution Resolution);
