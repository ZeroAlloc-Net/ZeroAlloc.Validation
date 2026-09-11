using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

[Validate]
public class AllocListCollectionModel
{
    [NotEmpty]
    public string Name { get; set; } = "";

#pragma warning disable MA0016 // deliberately concrete: pins the generated iteration for List<T>
    public List<AllocChildModel> Items { get; set; } = [];
#pragma warning restore MA0016
}
