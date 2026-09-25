using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Overrides the abstract method without the attribute and leaves it virtual.</summary>
[Validate]
public class InheritanceChainMiddle : InheritanceChainRoot
{
    public override IEnumerable<ValidationFailure> CheckBudget()
    {
        if (Budget < 0)
            yield return new ValidationFailure { PropertyName = nameof(Budget), ErrorMessage = "middle" };
    }
}
