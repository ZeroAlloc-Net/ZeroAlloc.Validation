using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class ExclusiveBetweenModel
{
    [ExclusiveBetween(0, 100)]
    public int Value { get; set; }
}
