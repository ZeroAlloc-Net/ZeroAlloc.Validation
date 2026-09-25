using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Validation.Inject;

/// <summary>
/// Shared helper used by InjectGenerator, AspNetCoreFilterEmitter, and OptionsValidationEmitter
/// to read the org-wide "ZeroAllocGeneratedAccessibility" MSBuild property (issue #193). None of
/// these three generators emit a diagnostic for an invalid value themselves — ZeroAlloc.Validation
/// is a mandatory dependency of every package that bundles one of them, and its own
/// ValidatorGenerator already reports ZV0019 once for the whole compilation. Reporting it again
/// here would duplicate the diagnostic for every generator combined into the same project.
/// </summary>
public static class GeneratedAccessibilityOption
{
    private const string PropertyKey = "build_property.ZeroAllocGeneratedAccessibility";

    /// <summary>
    /// True when the property is set to "Internal" (case-insensitive). Unset, empty, "Public",
    /// or any other (invalid) value all resolve to false, so an invalid value here falls back to
    /// today's public output, matching ValidatorGenerator's own fallback.
    /// </summary>
    public static bool IsInternal(AnalyzerConfigOptionsProvider provider)
    {
        return provider.GlobalOptions.TryGetValue(PropertyKey, out var raw)
            && raw.Length > 0
            && string.Equals(raw, "Internal", System.StringComparison.OrdinalIgnoreCase);
    }
}
