using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

// A generic model, issue #238, composed by Delivery as Crate<Parcel>.
[Validate]
public class Crate<TContent> where TContent : class
{
    [NotEmpty] public string Label { get; set; } = "";

    public IList<TContent> Contents { get; set; } = new List<TContent>();
}
