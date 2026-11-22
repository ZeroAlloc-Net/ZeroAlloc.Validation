using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// A generic model with a nested validator and behaviors applied to its open form: every closing's
/// chains thread the validator as their state and allocate no delegate, issues #298 and #410.
/// </summary>
[Validate]
public class AllocGenericBehaviorModel<T>
{
    [NotEmpty]
    public string Reference { get; set; } = "";

    public PipelineNestedLine? Line { get; set; }

    public T? Payload { get; set; }
}
