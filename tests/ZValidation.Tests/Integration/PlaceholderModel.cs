using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public partial class PlaceholderModel
{
    [NotEmpty(Message = "'{PropertyName}' is required")]
    public string Name { get; set; } = "";

    [GreaterThan(0, Message = "'{PropertyName}' must be greater than {ComparisonValue}")]
    public int Age { get; set; }

    [Length(2, 50, Message = "'{PropertyName}' must be {MinLength}\u201350 chars")]
    public string Bio { get; set; } = "";

    [ExclusiveBetween(0, 100, Message = "'{PropertyName}' must be between {From} and {To}")]
    public double Score { get; set; }
}
