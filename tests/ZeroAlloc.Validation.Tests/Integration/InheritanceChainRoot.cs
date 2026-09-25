using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Declares an abstract <c>[CustomValidation]</c> method that its subtypes override.</summary>
public abstract class InheritanceChainRoot
{
    public int Budget { get; init; }

    [CustomValidation]
    public abstract IEnumerable<ValidationFailure> CheckBudget();
}
