using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Inject;

/// <summary>
/// Shared helper used by InjectGenerator, AspNetCoreFilterEmitter, and OptionsValidationEmitter.
/// Emits one TryAddSingleton line per [Validate] class, registering the generated validator
/// as ValidatorFor&lt;T&gt; so any DI consumer can resolve it by the abstract base type.
/// </summary>
internal static class ValidatorRegistrationEmitter
{
    /// <summary>
    /// Appends one <c>services.TryAddSingleton&lt;ValidatorFor&lt;T&gt;, TValidator&gt;();</c>
    /// line per model into <paramref name="sb"/>.
    /// </summary>
    public static void EmitRegistrations(StringBuilder sb, IEnumerable<INamedTypeSymbol> models)
    {
        foreach (var model in models)
        {
            var modelFqn     = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var validatorFqn = model.ContainingNamespace.IsGlobalNamespace
                ? $"global::{model.Name}Validator"
                : $"global::{model.ContainingNamespace.ToDisplayString()}.{model.Name}Validator";

            sb.AppendLine(
                $"        services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<{modelFqn}>, {validatorFqn}>();");
        }
    }
}
