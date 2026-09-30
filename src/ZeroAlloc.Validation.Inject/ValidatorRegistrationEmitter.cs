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
    /// <para>
    /// A closing of a generic model, such as <c>Page&lt;Order&gt;</c> reached from a property of a
    /// non-generic model, is registered closed, <c>ValidatorFor&lt;Page&lt;Order&gt;&gt;</c> as
    /// <c>PageValidator&lt;Order&gt;</c>, issue #238. An open-generic registration cannot express it:
    /// MS DI maps the service's type arguments to the implementation's by position. Each such line
    /// is followed by a <c>TryAddEnumerable</c> entry that lists the validator as an
    /// <c>IModelValidator</c>, resolved through the <c>ValidatorFor</c> registration, so a
    /// registration the application made first is the one listed. <c>TryAddEnumerable</c> keeps
    /// one entry per closing however many registrations name it. A non-generic model gets no entry.
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
                AppendNode(sb, root);
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
            if (registered.Contains(key) || nodes[key].Registration is null)
                return;

            registered.Add(key);
            AppendNode(sb, nodes[key]);
            pending.Enqueue(nodes[key]);
        }
    }

    private static void AppendNode(StringBuilder sb, RegistrationNode node)
    {
        sb.AppendLine(node.Registration);
        if (node.RegistryEntry is { } entry)
            sb.AppendLine(entry);
    }

    /// <summary>
    /// The registrations <paramref name="model"/> needs, as data: its own node first, then one per
    /// model its validator's constructor takes, transitively, in the order they are reached. A
    /// model this compilation cannot name the validator of gets a node without a registration and
    /// is not followed. So does a type that still holds a type parameter, such as the
    /// <c>Line&lt;TItem&gt;</c> of an open <c>Page&lt;TItem&gt;</c>: nothing closed can be
    /// registered for it. A closing of a generic model is walked as that closing, so its
    /// properties have their substituted types, <c>Line&lt;Order&gt;</c> for <c>Page&lt;Order&gt;</c>.
    /// </summary>
    internal static EquatableArray<RegistrationNode> RegistrationGraph(INamedTypeSymbol model, Compilation compilation)
    {
        var nodes = new List<RegistrationNode>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { model.OriginalDefinition };
        var pending = new Queue<(INamedTypeSymbol Model, string? Registration)>();
        pending.Enqueue((model, RegistrableLine(model, GeneratedValidatorNames.QualifiedValidatorName(model))));

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

            nodes.Add(new RegistrationNode(Key(current), registration, RegistryEntry(current), EquatableArray.From(dependencies)));
        }

        return EquatableArray.From(nodes);

        string Visit(INamedTypeSymbol nested)
        {
            if (seen.Add(nested))
            {
                var validatorName = ValidatorNameIfAccessible(nested, compilation);
                pending.Enqueue((nested, validatorName is null ? null : RegistrableLine(nested, validatorName)));
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
    /// <see cref="ValidatorForLine"/>, or <see langword="null"/> when <paramref name="model"/> still
    /// holds a type parameter, which no closed registration can name.
    /// </summary>
    private static string? RegistrableLine(INamedTypeSymbol model, string validatorFqn) =>
        ContainsTypeParameter(model) ? null : ValidatorForLine(model, validatorFqn);

    /// <summary>
    /// The line listing a generic model's closing in the <c>IModelValidator</c> registry, or
    /// <see langword="null"/> for a model that is not generic. The entry resolves the closing's
    /// <c>ValidatorFor</c> registration rather than constructing the generated validator, so a
    /// registration the application made first is the one listed.
    /// </summary>
    private static string? RegistryEntry(INamedTypeSymbol model)
    {
        if (!GeneratedValidatorReach.IsGeneric(model))
            return null;

        var service = $"global::ZeroAlloc.Validation.ValidatorFor<{model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";
        return "        services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<"
            + $"global::ZeroAlloc.Validation.IModelValidator, {service}>("
            + $"static sp => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{service}>(sp)));";
    }

    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
        INamedTypeSymbol named => (named.ContainingType is { } container && ContainsTypeParameter(container))
                                  || AnyContainsTypeParameter(named.TypeArguments),
        _ => false,
    };

    private static bool AnyContainsTypeParameter(System.Collections.Immutable.ImmutableArray<ITypeSymbol> types)
    {
        foreach (var type in types)
        {
            if (ContainsTypeParameter(type)) return true;
        }
        return false;
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
    /// Looked up by metadata name, whose namespace is never keyword-escaped. For a closing of a
    /// generic model it is the validator closed over the same type arguments.
    /// </summary>
    private static INamedTypeSymbol? ReferencedValidator(INamedTypeSymbol model, Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(model.ContainingAssembly, compilation.Assembly))
            return null;

        var validator = model.ContainingAssembly?.GetTypeByMetadataName(GeneratedValidatorNames.MetadataName(model));
        if (validator is null || !compilation.IsSymbolAccessibleWithin(validator, compilation.Assembly))
            return null;

        var arguments = GeneratedValidatorNames.TypeArguments(model);
        return arguments.Count == 0 || arguments.Count != validator.Arity
            ? validator
            : validator.Construct([.. arguments]);
    }

    private static bool IsConstructible(INamedTypeSymbol type, Compilation compilation) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && !type.IsUnboundGenericType
        && !type.IsStatic
        && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
}
