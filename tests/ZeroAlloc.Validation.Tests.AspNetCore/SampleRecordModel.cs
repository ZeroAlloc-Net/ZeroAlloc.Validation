using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

// A record, because the filter generator used to match class declarations only and let
// every [Validate] record through unvalidated.
[Validate]
public partial record SampleRecordModel
{
    [NotEmpty] public string Name { get; init; } = "";
}
