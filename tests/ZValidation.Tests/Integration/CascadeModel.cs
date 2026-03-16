using ZValidation;

namespace ZValidation.Tests.Integration;

[Validate]
public class CascadeModel
{
    // Stop mode: only first failure reported per property
    [StopOnFirstFailure]
    [NotEmpty]
    [MinLength(5)]
    [MaxLength(100)]
    public string StopName { get; set; } = "ok";

    // Continue mode (default): all failures reported independently
    [NotEmpty]
    [MinLength(5)]
    [MaxLength(100)]
    public string ContinueName { get; set; } = "ok";
}
