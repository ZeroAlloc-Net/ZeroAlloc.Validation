using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class Person
{
    [NotEmpty(Message = "Name is required.")]
    [ZValidation.MaxLength(100)]
    public string Name { get; set; } = "";

    [ZValidation.EmailAddress]
    public string Email { get; set; } = "";

    [GreaterThan(0)]
    [LessThan(120)]
    public int Age { get; set; }
}
