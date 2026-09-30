using System.Numerics;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A generic model over a number, issue #238: compared through <c>double.CreateChecked</c>.</summary>
[Validate]
public class GenericMeasure<T> where T : struct, INumber<T>
{
    [GreaterThan(0, Message = "{PropertyName} was {PropertyValue}.")]
    public T Amount { get; set; }

    [InclusiveBetween(1, 100)]
    public T? Optional { get; set; }

    [NotEmpty]
    public string Unit { get; set; } = "kg";
}
