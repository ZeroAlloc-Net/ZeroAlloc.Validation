using System;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

[Validate]
public sealed class DateRange
{
    public DateTime StartDate { get; set; }

    [Must(nameof(IsAfterStart))]
    public DateTime EndDate { get; set; }

    // Instance method — generator emits a direct call. Sees this.StartDate
    // for the cross-property comparison.
    // Signature per docs/attributes.md: bool MethodName(TPropType value).
    public bool IsAfterStart(DateTime endValue) => endValue > StartDate;
}
