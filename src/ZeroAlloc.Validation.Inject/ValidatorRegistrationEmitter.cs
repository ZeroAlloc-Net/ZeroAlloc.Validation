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
        var graphs = new List<EquatableArray<RegistrationNode>>();
        foreach (var model in models)
            graphs.Add(model.Registrations);
        AppendRegistrations(sb, graphs);
    }

    /// <summary>
    /// Appends the registrations of one model's graph, such as the one
    /// <see cref="OpenRegistrationGraph"/> extracted for a generic model's registration helper.
    /// </summary>
    internal static void AppendRegistrations(StringBuilder sb, EquatableArray<RegistrationNode> registrations) =>
        AppendRegistrations(sb, new List<EquatableArray<RegistrationNode>> { registrations });

    private static void AppendRegistrations(StringBuilder sb, List<EquatableArray<RegistrationNode>> graphs)
    {
        var nodes = new Dictionary<string, RegistrationNode>(StringComparer.Ordinal);
        var registered = new HashSet<string>(StringComparer.Ordinal);
        var validateWith = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<RegistrationNode>();

        for (var g = 0; g < graphs.Count; g++)
        {
            var registrations = graphs[g];
            foreach (var node in registrations)
            {
                if (!nodes.ContainsKey(node.Key))
                    nodes.Add(node.Key, node);
            }

            var root = registrations[0];
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
    internal static EquatableArray<RegistrationNode> RegistrationGraph(INamedTypeSymbol model, Compilation compilation) =>
        RegistrationGraph(model, compilation, RegistrationNames.Default, overTypeParameters: false);

    /// <summary>
    /// The registrations a generic method over <paramref name="model"/>'s type parameters needs,
    /// issue #238: the <c>Add…Validator&lt;…&gt;()</c> helper and the generic
    /// <c>ValidateWithZeroAlloc&lt;…&gt;()</c> overload, which close them at the call site. Like
    /// <see cref="RegistrationGraph(INamedTypeSymbol, Compilation)"/>, except that a type written
    /// in terms of the model's type parameters, the model itself or the <c>Line&lt;TItem&gt;</c>
    /// its validator takes, is registered too. Every type parameter the walk reaches is one of the
    /// model's or of a type containing it, since properties are walked with their substituted
    /// types. The lines use <paramref name="names"/>, which the method chooses so they do not clash
    /// with a type parameter of the same name.
    /// </summary>
    internal static EquatableArray<RegistrationNode> OpenRegistrationGraph(INamedTypeSymbol model, Compilation compilation, RegistrationNames names) =>
        RegistrationGraph(model, compilation, names, overTypeParameters: true);

    private static EquatableArray<RegistrationNode> RegistrationGraph(
        INamedTypeSymbol model, Compilation compilation, RegistrationNames names, bool overTypeParameters)
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
                    ? $"        {names.Services}.TryAddSingleton<{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>();"
                    : null;
                var followed = nestedModel is not null
                    && SymbolEqualityComparer.Default.Equals(type, ReferencedValidator(nestedModel, compilation))
                        ? Visit(nestedModel)
                        : null;
                dependencies.Add(new RegistrationDependency(true, Key(type), line, followed));
            }

            nodes.Add(new RegistrationNode(Key(current), registration, RegistryEntry(current, names), EquatableArray.From(dependencies)));
        }

        return EquatableArray.From(nodes);

        // A type that still holds a type parameter gets no closed registration, unless the lines
        // go in a method generic over the model's type parameters.
        string? RegistrableLine(INamedTypeSymbol type, string validatorFqn) =>
            !overTypeParameters && ContainsTypeParameter(type) ? null : ValidatorForLine(type, validatorFqn, names);

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

    private static string ValidatorForLine(INamedTypeSymbol model, string validatorFqn, RegistrationNames names)
    {
        var modelFqn = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return $"        {names.Services}.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<{modelFqn}>, {validatorFqn}>();";
    }

    /// <summary>
    /// The line listing a generic model's closing in the <c>IModelValidator</c> registry, or
    /// <see langword="null"/> for a model that is not generic. The entry resolves the closing's
    /// <c>ValidatorFor</c> registration rather than constructing the generated validator, so a
    /// registration the application made first is the one listed.
    /// </summary>
    private static string? RegistryEntry(INamedTypeSymbol model, RegistrationNames names)
    {
        if (!GeneratedValidatorReach.IsGeneric(model))
            return null;

        var service = $"global::ZeroAlloc.Validation.ValidatorFor<{model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";
        return $"        {names.Services}.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<"
            + $"global::ZeroAlloc.Validation.IModelValidator, {service}>("
            + $"static {names.Provider} => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{service}>({names.Provider})));";
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
