using System;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Allocation-free custom validation via a span over pre-built failures.</summary>
[Validate]
public class CustomValidationSpanModel
{
    private static readonly ValidationFailure[] NegativeBudget =
    [
        new ValidationFailure { PropertyName = "Budget", ErrorMessage = "Budget must not be negative." },
    ];

    [NotEmpty]
    public string Reference { get; set; } = "ok";

    public int Budget { get; set; }

    [CustomValidation]
    public ReadOnlySpan<ValidationFailure> ValidateBudget() =>
        Budget >= 0 ? ReadOnlySpan<ValidationFailure>.Empty : NegativeBudget;
}
