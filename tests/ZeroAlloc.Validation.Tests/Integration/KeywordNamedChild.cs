using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

// The nested and collection element model of KeywordNamedModel, issue #242.
[Validate]
public class KeywordNamedChild
{
    [NotEmpty] public string? @namespace { get; set; }
}
