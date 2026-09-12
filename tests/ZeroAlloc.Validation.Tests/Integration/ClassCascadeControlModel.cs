using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Control for the class-level cascade tests: no cascade, so every violated rule is reported.</summary>
[Validate]
public class ClassCascadeControlModel
{
    [NotEmpty]
    [MinLength(3)]
    public string Tenant { get; set; } = "";
}
