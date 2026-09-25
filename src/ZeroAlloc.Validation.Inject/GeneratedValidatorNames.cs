using System.Text;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// The one place that decides what the generated validator for a <c>[Validate]</c> model is
/// called, shared by ValidatorGenerator, which declares it, and by the Inject, Options and
/// ASP.NET Core generators and the nested-validator composition, which name it. Each of those
/// used to rebuild the name from <c>model.Name</c> on its own, which named a type that does
/// not exist as soon as the model was nested in another type, issue #207.
/// </summary>
/// <remarks>
/// The validator is always a top-level class in the model's namespace. A model declared at
/// namespace level keeps the name it always had, <c>{Model}Validator</c>. A model nested in
/// other types gets the names of every containing type, outermost first, joined to its own
/// name with underscores: <c>Outer.Request</c> becomes <c>Outer_RequestValidator</c> and
/// <c>A.B.Request</c> becomes <c>A_B_RequestValidator</c>. Using the plain model name for a
/// nested model would give <c>Outer.Request</c>, <c>Other.Request</c> and a top-level
/// <c>Request</c> the same <c>RequestValidator</c>; plain concatenation, <c>OuterRequestValidator</c>,
/// would collide with a top-level model named <c>OuterRequest</c>, a name that follows .NET
/// conventions. The underscore can only collide with a type whose own name contains an
/// underscore, which the .NET naming guidelines rule out. Such a collision, for example a
/// top-level <c>Outer_Request</c> next to <c>Outer.Request</c>, is reported as ZV0031 and neither
/// model gets a validator, issue #220; <see cref="GeneratedValidatorReach"/> finds it.
/// The validator cannot be nested inside the containing type instead: that would require every
/// containing type to be declared <c>partial</c>.
/// <para>
/// A namespace is written into code with each keyword segment escaped, <c>@class.Models</c>, and
/// into a hint name without the escape, which hint names do not allow, issue #242. Neither
/// relies on the default display format escaping keywords.
/// </para>
/// </remarks>
internal static class GeneratedValidatorNames
{
    private static readonly SymbolDisplayFormat CodeNamespaceFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat HintNamespaceFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    /// <summary>
    /// The namespace the validator for <paramref name="model"/> is declared in, as C# code with
    /// keyword segments escaped, or <see langword="null"/> for the global namespace.
    /// </summary>
    public static string? NamespaceName(INamedTypeSymbol model)
    {
        var ns = model.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? null : ns.ToDisplayString(CodeNamespaceFormat);
    }

    /// <summary>The validator's simple name, for example <c>Outer_RequestValidator</c>.</summary>
    public static string ValidatorName(INamedTypeSymbol model)
    {
        var sb = new StringBuilder();
        AppendContainers(sb, model.ContainingType);
        sb.Append(model.Name).Append("Validator");
        return sb.ToString();
    }

    /// <summary>
    /// The validator's fully qualified name, for example <c>global::Ns.Outer_RequestValidator</c>.
    /// </summary>
    public static string QualifiedValidatorName(INamedTypeSymbol model) =>
        NamespaceName(model) is { } ns
            ? $"global::{ns}.{ValidatorName(model)}"
            : $"global::{ValidatorName(model)}";

    /// <summary>
    /// The hint name of the validator's generated file, for example
    /// <c>Ns.Outer_RequestValidator.g.cs</c>. It includes the namespace, so same-named models in
    /// two namespaces do not produce the same hint name and fail the generator.
    /// </summary>
    public static string HintName(INamedTypeSymbol model)
    {
        var ns = model.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace
            ? $"{ValidatorName(model)}.g.cs"
            : $"{ns.ToDisplayString(HintNamespaceFormat)}.{ValidatorName(model)}.g.cs";
    }

    private static void AppendContainers(StringBuilder sb, INamedTypeSymbol? container)
    {
        if (container is null)
            return;

        AppendContainers(sb, container.ContainingType);
        sb.Append(container.Name).Append('_');
    }
}
