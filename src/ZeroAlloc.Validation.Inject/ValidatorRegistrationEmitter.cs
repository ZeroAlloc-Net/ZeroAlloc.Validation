using System;
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
        var infos = new List<ValidatedModelInfo>();
        foreach (var model in models)
            infos.Add(ValidatedModelInfo.From(model, compilation));
        AppendRegistrations(sb, infos);
    }

    /// <summary>
    /// Appends the registrations <see cref="EmitRegistrations"/> describes, from models whose
    /// registration graphs <see cref="RegistrationGraph"/> extracted, so the generators emit them
    /// without a symbol or a compilation, issue #209. The models are registered first, in order,
    /// then the validators their constructors take, breadth first, each once.
    /// </summary>
    internal static void AppendRegistrations(StringBuilder sb, IEnumerable<ValidatedModelInfo> models)
    {
        var nodes = new Dictionary<string, RegistrationNode>(StringComparer.Ordinal);
        var registered = new HashSet<string>(StringComparer.Ordinal);
        var validateWith = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<RegistrationNode>();

        foreach (var model in models)
        {
            foreach (var node in model.Registrations)
            {
                if (!nodes.ContainsKey(node.Key))
                    nodes.Add(node.Key, node);
            }

            var root = model.Registrations[0];
            if (registered.Add(root.Key))
            {
                sb.AppendLine(root.Registration);
                pending.Enqueue(root);
            }
        }

        while (pending.Count > 0)
        {
            foreach (var dependency in pending.Dequeue().Dependencies)
            {
                if (dependency.IsValidateWith)
                {
                    if (dependency.ValidateWithRegistration is { } line && validateWith.Add(dependency.Key))
                        sb.AppendLine(line);

                    // [ValidateWith] naming a referenced assembly's generated validator for the
                    // property's own model: that validator's constructor takes the model's nested
                    // validators, so the model is followed too.
                    if (dependency.FollowedModelKey is { } followed)
                        Register(followed);
                    continue;
                }

                Register(dependency.Key);
            }
        }

        void Register(string key)
        {
            if (registered.Contains(key) || nodes[key].Registration is not { } line)
                return;

            registered.Add(key);
            sb.AppendLine(line);
            pending.Enqueue(nodes[key]);
        }
    }

    /// <summary>
    /// The registrations <paramref name="model"/> needs, as data: its own node first, then one per
    /// model its validator's constructor takes, transitively, in the order they are reached. A
    /// model this compilation cannot name the validator of gets a node without a registration and
    /// is not followed.
    /// </summary>
    internal static EquatableArray<RegistrationNode> RegistrationGraph(INamedTypeSymbol model, Compilation compilation)
    {
        var nodes = new List<RegistrationNode>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { model.OriginalDefinition };
        var pending = new Queue<(INamedTypeSymbol Model, string? Registration)>();
        pending.Enqueue((model, ValidatorForLine(model, GeneratedValidatorNames.QualifiedValidatorName(model))));

        while (pending.Count > 0)
        {
            var (current, registration) = pending.Dequeue();
            if (registration is null)
            {
                nodes.Add(new RegistrationNode(Key(current), null, EquatableArray<RegistrationDependency>.Empty));
                continue;
            }

            var dependencies = new List<RegistrationDependency>();
            foreach (var (_, type, isValidateWith, nestedModel) in ValidatorDependencies.Of(current, compilation))
            {
                if (!isValidateWith)
                {
                    dependencies.Add(new RegistrationDependency(false, Visit(type), null, null));
                    continue;
                }

                var line = IsConstructible(type, compilation)
                    ? $"        services.TryAddSingleton<{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>();"
                    : null;
                var followed = nestedModel is not null
                    && SymbolEqualityComparer.Default.Equals(type, ReferencedValidator(nestedModel, compilation))
                        ? Visit(nestedModel)
                        : null;
                dependencies.Add(new RegistrationDependency(true, Key(type), line, followed));
            }

            nodes.Add(new RegistrationNode(Key(current), registration, EquatableArray.From(dependencies)));
        }

        return EquatableArray.From(nodes);

        string Visit(INamedTypeSymbol nested)
        {
            if (seen.Add(nested))
            {
                var validatorName = ValidatorNameIfAccessible(nested, compilation);
                pending.Enqueue((nested, validatorName is null ? null : ValidatorForLine(nested, validatorName)));
            }
            return Key(nested);
        }
    }

    // A type's identity across compilations: its assembly and fully qualified name, which tell
    // apart the same types SymbolEqualityComparer.Default does.
    private static string Key(INamedTypeSymbol type) =>
        type.ContainingAssembly?.Identity.Name + "|" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string ValidatorForLine(INamedTypeSymbol model, string validatorFqn)
    {
        var modelFqn = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return $"        services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<{modelFqn}>, {validatorFqn}>();";
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
