using System;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A generic model over an enum, issue #238: checked with <c>Enum.IsDefined&lt;T&gt;</c>.</summary>
[Validate]
public readonly record struct GenericKind<T>([property: IsInEnum] T Kind, [property: IsInEnum] T? Fallback)
    where T : struct, Enum;
