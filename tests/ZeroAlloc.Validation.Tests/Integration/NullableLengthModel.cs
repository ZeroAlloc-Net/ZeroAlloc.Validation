using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// A nullable property carrying length rules. Before the null guard this model could not even be
/// compiled by a consumer with nullable warnings as errors, and threw at runtime on a null value.
/// </summary>
[Validate]
public class NullableLengthModel
{
    [NotEmpty]
    [MinLength(3)]
    public string? Tenant { get; set; }

    [MaxLength(5)]
    public string? Region { get; set; }

    [Length(2, 4)]
    public string? Code { get; set; }

    [MinLength(2)]
    public int[]? Items { get; set; }
}
