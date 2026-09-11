using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public class InheritanceCustomBase
{
    public int Budget { get; init; }

    [CustomValidation]
    public IEnumerable<ValidationFailure> ValidateBudget()
    {
        if (Budget < 0)
            yield return new ValidationFailure
            {
                PropertyName = nameof(Budget),
                ErrorMessage = "Budget must not be negative."
            };
    }
}
