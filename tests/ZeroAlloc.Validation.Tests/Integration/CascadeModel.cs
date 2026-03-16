using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

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

    public bool ConditionalCheck { get; set; }

    // Stop fires only when first rule adds a failure — not merely when When skips it.
    [StopOnFirstFailure]
    [NotEmpty(When = nameof(IsConditionalRequired))]
    [MinLength(3)]
    public string ConditionalStop { get; set; } = "";
    public bool IsConditionalRequired() => ConditionalCheck;
}
