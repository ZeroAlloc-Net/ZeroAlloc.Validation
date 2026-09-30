using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// The one place that decides what the generated validator for a <c>[Validate]</c> model is
/// called, shared by ValidatorGenerator, which declares it, and by the Inject, Options and
/// ASP.NET Core generators, which register it. Each of those
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
/// <para>
/// A generic model, or one declared inside a generic type, gets a generic validator with the same
/// simple name, issue #238. Its type parameters are those of every containing type, outermost
/// first, then the model's own, with their declared names: <c>Page&lt;TItem&gt;</c> gets
/// <c>PageValidator&lt;TItem&gt;</c> and <c>Envelope&lt;T&gt;.Header</c> gets
/// <c>Envelope_HeaderValidator&lt;T&gt;</c>. The arity sets it apart from the validator of a
/// non-generic model of the same name, so the metadata and hint names carry it as a backtick
/// suffix, <c>Ns.PageValidator`1</c>, and <c>Box</c> and <c>Box&lt;T&gt;</c> do not add the same file.
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
    /// The validator of a generic model takes the model's type arguments: the declared names for
    /// the model itself, <c>global::Ns.PageValidator&lt;TItem&gt;</c>, and the fully qualified
    /// arguments for a closing of it, <c>global::Ns.PageValidator&lt;global::Ns.Order&gt;</c>.
    /// </summary>
    public static string QualifiedValidatorName(INamedTypeSymbol model)
    {
        var name = NamespaceName(model) is { } ns
            ? $"global::{ns}.{ValidatorName(model)}"
            : $"global::{ValidatorName(model)}";

        var arguments = TypeArguments(model);
        if (arguments.Count == 0)
            return name;

        var sb = new StringBuilder(name).Append('<');
        for (var i = 0; i < arguments.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(arguments[i].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }
        return sb.Append('>').ToString();
    }

    /// <summary>
    /// The type arguments of <paramref name="model"/> and of every type containing it, outermost
    /// first: the validator's type arguments. For a generic model as declared they are its type
    /// parameters; for a closing of it, the types it is closed over. Empty for a model that is not
    /// generic and is not declared inside a generic type.
    /// </summary>
    public static List<ITypeSymbol> TypeArguments(INamedTypeSymbol model)
    {
        var arguments = new List<ITypeSymbol>();
        AppendTypeArguments(arguments, model);
        return arguments;
    }

    /// <summary>
    /// The number of type parameters of <paramref name="model"/> and of every type containing it,
    /// which is the arity of its validator.
    /// </summary>
    public static int TotalArity(INamedTypeSymbol model)
    {
        var arity = 0;
        for (INamedTypeSymbol? type = model; type is not null; type = type.ContainingType)
            arity += type.Arity;
        return arity;
    }

    /// <summary>
    /// The validator's metadata name, for <c>GetTypeByMetadataName</c>, for example
    /// <c>class.Models.Outer_RequestValidator</c>: namespace segments are never escaped there,
    /// so a keyword namespace written <c>@class.Models</c> in code is looked up as
    /// <c>class.Models</c>, issue #246. A generic validator's name ends with its arity,
    /// <c>Ns.PageValidator`1</c>, as metadata names do.
    /// </summary>
    public static string MetadataName(INamedTypeSymbol model)
    {
        var ns = model.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace
            ? ValidatorName(model) + AritySuffix(model)
            : $"{ns.ToDisplayString(HintNamespaceFormat)}.{ValidatorName(model)}{AritySuffix(model)}";
    }

    /// <summary>
    /// The hint name of the validator's generated file, for example
    /// <c>Ns.Outer_RequestValidator.g.cs</c>. It includes the namespace, so same-named models in
    /// two namespaces do not produce the same hint name and fail the generator, and the arity of a
    /// generic validator, <c>Ns.PageValidator`1.g.cs</c>, so <c>Box</c> and <c>Box&lt;T&gt;</c> do not either.
    /// </summary>
    public static string HintName(INamedTypeSymbol model)
    {
        var ns = model.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace
            ? $"{ValidatorName(model)}{AritySuffix(model)}.g.cs"
            : $"{ns.ToDisplayString(HintNamespaceFormat)}.{ValidatorName(model)}{AritySuffix(model)}.g.cs";
    }

    /// <summary>
    /// The backtick and arity a generic validator's metadata name ends with, <c>`1</c>, or empty
    /// for a validator that is not generic, whose names stay exactly as they were.
    /// </summary>
    private static string AritySuffix(INamedTypeSymbol model)
    {
        var arity = TotalArity(model);
        return arity == 0 ? "" : "`" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AppendTypeArguments(List<ITypeSymbol> arguments, INamedTypeSymbol type)
    {
        if (type.ContainingType is { } container)
            AppendTypeArguments(arguments, container);
        arguments.AddRange(type.TypeArguments);
    }

    private static void AppendContainers(StringBuilder sb, INamedTypeSymbol? container)
    {
        if (container is null)
            return;

        AppendContainers(sb, container.ContainingType);
        sb.Append(container.Name).Append('_');
    }
}
