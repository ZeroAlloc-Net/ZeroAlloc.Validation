using System.Collections.Generic;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Static fields a generated validator declares, collected while its rules are emitted. The sync
/// and async emit paths share one instance, so a field referenced from both is declared once.
/// </summary>
internal sealed class GeneratedFields
{
    /// <summary><c>[Matches]</c> regex fields: field name to pattern.</summary>
    public Dictionary<string, string> RegexPatterns { get; } = new(StringComparer.Ordinal);

    /// <summary>User-defined rule instances: field name to the declaration to emit.</summary>
    public Dictionary<string, RuleInstanceField> RuleInstances { get; } = new(StringComparer.Ordinal);
}
