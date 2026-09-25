using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public class InheritanceOverrideBase
{
    public int Budget { get; init; }

    [CustomValidation]
    public virtual IEnumerable<ValidationFailure> CheckBudget()
    {
        if (Budget < 0)
            yield return new ValidationFailure { PropertyName = nameof(Budget), ErrorMessage = "base" };
    }
}
