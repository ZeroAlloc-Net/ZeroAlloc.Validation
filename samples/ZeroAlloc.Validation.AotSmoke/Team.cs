using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

// No asynchronous rule of its own: its nested and collection members have them, so its validator
// awaits theirs.
[Validate]
public sealed class Team
{
    [NotEmpty] public string Name { get; set; } = "";

    public Member? Lead { get; set; }

    public IReadOnlyList<Member> Members { get; set; } = [];
}
