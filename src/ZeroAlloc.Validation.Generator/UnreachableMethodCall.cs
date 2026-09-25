using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// A rule's call to a model method the generated validator cannot make; see
/// <see cref="RuleEmitter.GetUnreachableMethodCalls"/>.
/// </summary>
internal readonly record struct UnreachableMethodCall(
    AttributeData Attribute,
    string Usage,
    string MethodName,
    MethodReach Reach,
    IMethodSymbol? Method);
