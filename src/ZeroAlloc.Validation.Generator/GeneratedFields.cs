using System.Collections.Generic;
using System.Text;

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

    /// <summary>
    /// Declares the collected fields: the generated validator after both emit paths have run,
    /// and <see cref="RuleEmitter.EmitWarningProbe"/> after the body it compiles.
    /// </summary>
    public void AppendDeclarations(StringBuilder sb)
    {
        AppendRegexFields(sb);
        AppendRuleInstanceFields(sb);
    }

    /// <summary>
    /// Emits one <c>private static readonly Regex</c> field per unique
    /// <c>[Matches]</c>-decorated property collected during rule emission.
    /// Initialised with <c>RegexOptions.Compiled</c> so the matcher is JIT'd
    /// once and the per-call hot path is a direct method dispatch.
    /// </summary>
    /// <remarks>
    /// Initial design called for <c>[GeneratedRegex]</c> partial methods, but
    /// Roslyn source generators cannot see syntax trees added by other
    /// generators in the same compilation pass — the .NET RegexGenerator
    /// never sees our partial method declarations and never emits the
    /// implementation half (CS8795). Static compiled-Regex fields give the
    /// bulk of the perf win without the inter-generator visibility
    /// dependency.
    /// </remarks>
    private void AppendRegexFields(StringBuilder sb)
    {
        foreach (var kvp in RegexPatterns)
        {
            var fieldName = kvp.Key;
            var pattern = kvp.Value;
            sb.AppendLine();
            sb.AppendLine($"    private static readonly global::System.Text.RegularExpressions.Regex {fieldName}");
            sb.AppendLine($"        = new global::System.Text.RegularExpressions.Regex(\"{RuleEmitter.EscapeString(pattern)}\", global::System.Text.RegularExpressions.RegexOptions.Compiled);");
        }
    }

    /// <summary>
    /// Emits one <c>private static readonly</c> field per user-defined rule usage, holding the
    /// attribute rebuilt from its constructor and named arguments. The instance is created once,
    /// when the validator type initialises, so each validation call only invokes <c>IsValid</c>.
    /// A declaration that names an <c>[Obsolete]</c> symbol is wrapped in a pragma for CS0618 and
    /// CS0612. The compiler already warns at the usage in user code, where the user can act on
    /// it; the repeat inside generated code cannot be suppressed by the user and would break a
    /// <c>TreatWarningsAsErrors</c> build. Every other declaration is emitted without a pragma.
    /// </summary>
    private void AppendRuleInstanceFields(StringBuilder sb)
    {
        foreach (var kvp in RuleInstances)
        {
            sb.AppendLine();
            if (kvp.Value.IsObsolete)
            {
                sb.AppendLine("    // The rule's initializer names an obsolete symbol; the compiler already warns at the attribute usage in user code.");
                sb.AppendLine("#pragma warning disable CS0618, CS0612");
            }
            sb.AppendLine($"    private static readonly {kvp.Value.TypeName} {kvp.Key}");
            sb.AppendLine($"        = {kvp.Value.Initializer};");
            if (kvp.Value.IsObsolete)
                sb.AppendLine("#pragma warning restore CS0618, CS0612");
        }
    }
}
