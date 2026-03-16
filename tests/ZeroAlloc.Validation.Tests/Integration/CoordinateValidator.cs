using ZValidation;

namespace ZValidation.Tests.Integration;

// Hand-written validator for a third-party type. NOT source-generated.
public class CoordinateValidator : ValidatorFor<Coordinate>
{
    public override ValidationResult Validate(Coordinate instance)
    {
        var failures = new System.Collections.Generic.List<ValidationFailure>();
        if (instance.Lat < -90 || instance.Lat > 90)
            failures.Add(new ValidationFailure
            {
                PropertyName = "Lat",
                ErrorMessage = "Latitude must be between -90 and 90.",
                ErrorCode = "LAT_RANGE"
            });
        if (instance.Lng < -180 || instance.Lng > 180)
            failures.Add(new ValidationFailure
            {
                PropertyName = "Lng",
                ErrorMessage = "Longitude must be between -180 and 180.",
                ErrorCode = "LNG_RANGE"
            });
        return new ValidationResult(failures.ToArray());
    }
}
