using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A generic model a behavior applies to by its open form, issue #238.</summary>
[Validate]
public class GenericAudited<T>
{
    [NotEmpty]
    public string Reference { get; set; } = "";

    public GenericLine<GenericProduct>? Line { get; set; }
}
