using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate(StopOnFirstFailure = true)]
public class AllocFailFastModel
{
    [NotEmpty]
    public string Name { get; set; } = "";

    [GreaterThan(0)]
    public int Age { get; set; }
}
