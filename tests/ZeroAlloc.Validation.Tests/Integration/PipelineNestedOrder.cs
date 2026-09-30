using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// A model with pipeline behaviors and nested validators, issue #294: its generated validator did
/// not compile, since the behaviors' chain was built from static lambdas.
/// </summary>
[Validate]
public class PipelineNestedOrder
{
    [NotEmpty] public string Reference { get; set; } = "";

    public PipelineNestedLine? Line { get; set; }

    public IReadOnlyList<PipelineNestedLine>? Lines { get; set; }
}
