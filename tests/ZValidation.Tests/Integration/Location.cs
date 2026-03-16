using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class Location
{
    [NotEmpty]
    public string Name { get; set; } = "";

    [ValidateWith(typeof(CoordinateValidator))]
    public Coordinate Point { get; set; } = new();
}
