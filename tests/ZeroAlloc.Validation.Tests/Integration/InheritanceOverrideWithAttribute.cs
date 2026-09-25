using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Overrides a <c>[CustomValidation]</c> method and repeats the attribute.</summary>
[Validate]
public class InheritanceOverrideWithAttribute : InheritanceOverrideBase
{
    [CustomValidation]
    public override IEnumerable<ValidationFailure> CheckBudget()
    {
        if (Budget < 0)
            yield return new ValidationFailure { PropertyName = nameof(Budget), ErrorMessage = "override" };
    }
}
