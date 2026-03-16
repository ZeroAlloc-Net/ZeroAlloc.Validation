namespace ZeroAlloc.Validation.Tests.Integration;

// Intentionally no [Validate] — simulates a third-party type you don't control.
public class Coordinate
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}
