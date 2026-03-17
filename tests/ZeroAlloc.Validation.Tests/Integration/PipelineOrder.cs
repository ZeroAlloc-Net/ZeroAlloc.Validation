using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class PipelineOrder
{
    [NotEmpty] public string Reference { get; set; } = "";
}
