using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class Person
{
    [NotEmpty(Message = "Name is required.")]
    [ZeroAlloc.Validation.MaxLength(100)]
    public string Name { get; set; } = "";

    [ZeroAlloc.Validation.EmailAddress]
    public string Email { get; set; } = "";

    [GreaterThan(0)]
    [LessThan(120)]
    public int Age { get; set; }
}
