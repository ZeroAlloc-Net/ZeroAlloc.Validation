using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// A generic model with a nested validator and behaviors applied to its open form: every closing's
/// chains cache their delegates, issue #298.
/// </summary>
[Validate]
public class AllocGenericBehaviorModel<T>
{
    [NotEmpty]
    public string Reference { get; set; } = "";

    public PipelineNestedLine? Line { get; set; }

    public T? Payload { get; set; }
}
