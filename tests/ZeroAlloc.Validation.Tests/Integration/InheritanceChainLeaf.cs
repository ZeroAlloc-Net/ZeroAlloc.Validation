using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Seals the override, again without the attribute.</summary>
[Validate]
public class InheritanceChainLeaf : InheritanceChainMiddle
{
    public sealed override IEnumerable<ValidationFailure> CheckBudget()
    {
        if (Budget < 0)
            yield return new ValidationFailure { PropertyName = nameof(Budget), ErrorMessage = "leaf" };
    }
}
