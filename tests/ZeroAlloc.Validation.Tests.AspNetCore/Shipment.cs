using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

// An action argument whose validator composes others, issue #246.
[Validate]
public class Shipment
{
    public Parcel Parcel { get; set; } = new();

    public IList<Parcel> Extras { get; set; } = new List<Parcel>();
}
