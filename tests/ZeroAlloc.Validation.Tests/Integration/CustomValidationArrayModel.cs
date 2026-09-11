using System;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Allocation-free custom validation: returns a cached empty array when there is nothing to report.</summary>
[Validate]
public class CustomValidationArrayModel
{
    [NotEmpty]
    public string Reference { get; set; } = "ok";

    public int Budget { get; set; }

    [CustomValidation]
    public ValidationFailure[] ValidateBudget() =>
        Budget >= 0
            ? Array.Empty<ValidationFailure>()
            : [new ValidationFailure { PropertyName = nameof(Budget), ErrorMessage = "Budget must not be negative." }];
}
