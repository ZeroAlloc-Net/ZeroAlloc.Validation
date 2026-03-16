using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class Location
{
    [NotEmpty]
    public string Name { get; set; } = "";

    [ValidateWith(typeof(CoordinateValidator))]
    public Coordinate Point { get; set; } = new();
}
