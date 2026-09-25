using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

// Nested and collection properties whose names differ only in the case of the first letter.
// Each pair camel-cases to one name, so the later property's validator field and constructor
// parameter take a numeric suffix.
[Validate]
public class CaseOnlyNamedModel
{
    public KeywordNamedChild? Address { get; set; }

    public KeywordNamedChild? address { get; set; }

    public IList<KeywordNamedChild> Items { get; set; } = new List<KeywordNamedChild>();

    public IList<KeywordNamedChild> items { get; set; } = new List<KeywordNamedChild>();
}
