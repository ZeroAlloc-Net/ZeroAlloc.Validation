using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Overrides a <c>[CustomValidation]</c> method without repeating the attribute.</summary>
[Validate]
public class InheritanceOverrideWithoutAttribute : InheritanceOverrideBase
{
    public override IEnumerable<ValidationFailure> CheckBudget()
    {
        if (Budget < 0)
            yield return new ValidationFailure { PropertyName = nameof(Budget), ErrorMessage = "override" };
    }
}
