using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>The nested model of <see cref="PipelineNestedOrder"/>.</summary>
[Validate]
public class PipelineNestedLine
{
    [NotEmpty] public string Sku { get; set; } = "";
}
