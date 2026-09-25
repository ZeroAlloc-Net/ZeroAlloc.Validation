using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Inject;

/// <summary>
/// Shared helper used by InjectGenerator, AspNetCoreFilterEmitter, and OptionsValidationEmitter.
/// Emits one TryAddSingleton line per [Validate] class, registering the generated validator
/// as ValidatorFor&lt;T&gt; so any DI consumer can resolve it by the abstract base type, and one
/// per validator those validators take in their constructors, so the container can build a
/// validator that composes others, issue #246.
/// </summary>
public static class ValidatorRegistrationEmitter
{
    /// <summary>
    /// Appends one <c>services.TryAddSingleton&lt;ValidatorFor&lt;T&gt;, TValidator&gt;();</c>
    /// line per model into <paramref name="sb"/>, then one for each validator a registered
    /// validator's constructor takes, transitively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A nested or collection <c>[Validate]</c> model is taken as <c>ValidatorFor&lt;TModel&gt;</c>
    /// and registered the same way as <paramref name="models"/>. That covers a model the caller
    /// did not pass, such as the nested models of the one options model
    /// <c>ValidateWithZeroAlloc</c> registers, and a model declared in a referenced assembly,
    /// whose validator this compilation's <c>[Validate]</c> scan never sees. A referenced
    /// assembly's validator that this assembly cannot name, an internal one, is left to that
    /// assembly's own registration.
    /// </para>
    /// <para>
    /// A <c>[ValidateWith(typeof(X))]</c> validator is taken as <c>X</c> and registered as
    /// <c>TryAddSingleton&lt;X&gt;()</c>, unless the container could not construct it: an abstract
    /// type, an interface or an open generic. Every line is a <c>TryAdd</c>, so a registration
    /// the application made first wins.
    /// </para>
    /// </remarks>
    public static void EmitRegistrations(StringBuilder sb, IEnumerable<INamedTypeSymbol> models, Compilation compilation)
    {
        var registered = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var validateWith = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<INamedTypeSymbol>();

        foreach (var model in models)
        {
            if (registered.Add(model.OriginalDefinition))
            {
                AppendValidatorFor(sb, model, GeneratedValidatorNames.QualifiedValidatorName(model));
                pending.Enqueue(model);
            }
        }

        while (pending.Count > 0)
        {
            foreach (var (_, type, isValidateWith, model) in ValidatorDependencies.Of(pending.Dequeue(), compilation))
            {
                if (isValidateWith)
                {
                    if (IsConstructible(type, compilation) && validateWith.Add(type))
                    {
                        sb.AppendLine(
                            $"        services.TryAddSingleton<{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>();");
                    }

                    // [ValidateWith] naming a referenced assembly's generated validator for the
                    // property's own model: that validator's constructor takes the model's nested
                    // validators, so the model is followed too.
                    if (model is not null
                        && SymbolEqualityComparer.Default.Equals(type, ReferencedValidator(model, compilation)))
                        Register(model);
                    continue;
                }

                Register(type);
            }
        }

        void Register(INamedTypeSymbol nested)
        {
            if (registered.Contains(nested) || ValidatorNameIfAccessible(nested, compilation) is not { } validatorName)
                return;

            registered.Add(nested);
            AppendValidatorFor(sb, nested, validatorName);
            pending.Enqueue(nested);
        }
    }

    private static void AppendValidatorFor(StringBuilder sb, INamedTypeSymbol model, string validatorFqn)
    {
        var modelFqn = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        sb.AppendLine(
            $"        services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<{modelFqn}>, {validatorFqn}>();");
    }

    /// <summary>
    /// The fully qualified name of <paramref name="model"/>'s generated validator when this
    /// compilation can name it, otherwise <see langword="null"/>. A model of this compilation
    /// always gets one, since <see cref="ValidatorDependencies"/> lists only models that do. A
    /// referenced assembly's validator is looked up, and must exist and be accessible here.
    /// </summary>
    private static string? ValidatorNameIfAccessible(INamedTypeSymbol model, Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(model.ContainingAssembly, compilation.Assembly))
            return GeneratedValidatorNames.QualifiedValidatorName(model);

        return ReferencedValidator(model, compilation) is not null
            ? GeneratedValidatorNames.QualifiedValidatorName(model)
            : null;
    }

    /// <summary>
    /// <paramref name="model"/>'s generated validator, declared in the referenced assembly that
    /// declares the model, when this compilation can access it, otherwise <see langword="null"/>.
    /// Looked up by metadata name, whose namespace is never keyword-escaped.
    /// </summary>
    private static INamedTypeSymbol? ReferencedValidator(INamedTypeSymbol model, Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(model.ContainingAssembly, compilation.Assembly))
            return null;

        var validator = model.ContainingAssembly?.GetTypeByMetadataName(GeneratedValidatorNames.MetadataName(model));
        return validator is not null && compilation.IsSymbolAccessibleWithin(validator, compilation.Assembly)
            ? validator
            : null;
    }

    private static bool IsConstructible(INamedTypeSymbol type, Compilation compilation) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && !type.IsUnboundGenericType
        && !type.IsStatic
        && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
}
