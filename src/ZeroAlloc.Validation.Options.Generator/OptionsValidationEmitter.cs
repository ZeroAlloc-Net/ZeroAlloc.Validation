using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Validation.Generator.Shared;
using ZeroAlloc.Validation.Inject;

namespace ZeroAlloc.Validation.Options.Generator;

[Generator]
public sealed class OptionsValidationEmitter : IIncrementalGenerator
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

    /// <summary>Tracking name of the step the output is produced from, so tests can assert that it stays cached.</summary>
    internal const string OutputTrackingName = "OptionsValidationModels";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validateClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateAttributeFqn,
                // RecordDeclarationSyntax is a sibling of ClassDeclarationSyntax under
                // TypeDeclarationSyntax, not a subtype, so matching only the latter skipped
                // every [Validate] record. The Inject generator had the same defect, #174.
                // A record struct is also a RecordDeclarationSyntax, so it is excluded by
                // kind: OptionsBuilder<T> requires a reference type and an overload for a
                // value type would not compile.
                predicate: static (node, _) => node is ClassDeclarationSyntax
                                               || node.IsKind(SyntaxKind.RecordDeclaration),
                // A model that gets no validator is left out here too: one the generated validator
                // cannot reach, ZV0025 and #216, or one whose type parameters it cannot redeclare,
                // ZV0029 and #219. Naming it would only add compiler errors in generated code. A
                // generic model is left out as a root, since nothing closed can be registered for
                // it; its closings are registered by the models composing them, and it gets a generic
                // overload, emitted from genericClasses below, issue #238.
                // Null marks it, and the step after Collect drops it.
                // The transform extracts everything the output needs into an equatable model, so
                // the output step stays cached while nothing it was read from changes, issue #209.
                // It reruns for every compilation, so the model still follows edits in other
                // files, such as to a nested model, and into referenced assemblies, issue #246.
                transform: static (ctx, _) =>
                    GeneratedValidatorReach.HasGeneratedValidator((INamedTypeSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation)
                    && !GeneratedValidatorReach.IsGeneric((INamedTypeSymbol)ctx.TargetSymbol)
                        ? ValidatedModelInfo.From((INamedTypeSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation)
                        : null);

        // A generic model gets one overload generic over its type parameters, which inference
        // closes from the builder, AddOptions<Page<Product>>().ValidateWithZeroAlloc(), issue #238.
        var genericClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax
                                               || node.IsKind(SyntaxKind.RecordDeclaration),
                transform: static (ctx, _) =>
                    GeneratedValidatorReach.HasGeneratedValidator((INamedTypeSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation)
                    && GeneratedValidatorReach.IsGeneric((INamedTypeSymbol)ctx.TargetSymbol)
                        ? GenericModelInfo.From((INamedTypeSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation)
                        : null);

        var collected = validateClasses.Collect()
            .Select(static (models, _) => ValidatedModelInfo.WithGeneratedValidator(models));
        var collectedGeneric = genericClasses.Collect()
            .Select(static (models, _) => GenericModelInfo.Collected(models));
        var isInternalMode = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => GeneratedAccessibilityOption.IsInternal(provider));
        var combined = collected.Combine(collectedGeneric).Combine(isInternalMode).WithTrackingName(OutputTrackingName);
        context.RegisterSourceOutput(combined, static (ctx, input) => Emit(ctx, input.Left.Left, input.Left.Right, input.Right));
    }

    private static void Emit(
        SourceProductionContext ctx,
        EquatableArray<ValidatedModelInfo> models,
        EquatableArray<GenericModelInfo> genericModels,
        bool isInternalMode)
    {
        if (models.Count == 0 && genericModels.Count == 0) return;

        // An extension method cannot be more visible than the model in its signature, so
        // models that are not visible outside the assembly go in a separate internal class,
        // issue #184. Public models keep the public class exactly as before, unless
        // ZeroAllocGeneratedAccessibility=Internal (issue #193) routes every model into the
        // internal class regardless of the model's own accessibility, so the public class is
        // never emitted at all — nothing generated becomes more visible than requested.
        var publicModels   = new List<ValidatedModelInfo>();
        var internalModels = new List<ValidatedModelInfo>();
        foreach (var model in models)
            (!isInternalMode && model.IsEffectivelyPublic ? publicModels : internalModels).Add(model);

        // A generic model's overload goes in the same classes, by the same rule.
        var publicGeneric   = new List<GenericModelInfo>();
        var internalGeneric = new List<GenericModelInfo>();
        foreach (var model in genericModels)
            (!isInternalMode && model.IsEffectivelyPublic ? publicGeneric : internalGeneric).Add(model);

        if (publicModels.Count > 0 || publicGeneric.Count > 0)
        {
            ctx.AddSource(
                "ZeroAlloc.Validation.ZeroAllocOptionsValidationExtensions.g.cs",
                EmitExtensionsClass("public", "ZeroAllocOptionsValidationExtensions", publicModels, publicGeneric));
        }

        if (internalModels.Count > 0 || internalGeneric.Count > 0)
        {
            ctx.AddSource(
                "ZeroAlloc.Validation.InternalZeroAllocOptionsValidationExtensions.g.cs",
                EmitExtensionsClass("internal", "InternalZeroAllocOptionsValidationExtensions", internalModels, internalGeneric));
        }
    }

    private static string EmitExtensionsClass(
        string accessibility, string className, List<ValidatedModelInfo> models, List<GenericModelInfo> genericModels)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine("using Microsoft.Extensions.Options;");
        sb.AppendLine();
        // Namespaced next to ZeroAllocOptionsValidator<T>, issue #193 point 2: emitting a
        // public type into the global namespace meant every library using this generator
        // shipped a same-named public type in the one namespace every consumer shares, and
        // callers relying on the old global-namespace lookup now need a using directive.
        sb.AppendLine("namespace ZeroAlloc.Validation.Options;");
        sb.AppendLine();
        // Documented rather than suppressed with #pragma warning disable CS1591: the
        // <auto-generated> header does not cover compiler diagnostics, and real documentation
        // also reaches the consumer's own XML documentation file.
        sb.AppendLine("/// <summary>Connects the generated validators to the options validation pipeline.</summary>");
        sb.AppendLine($"{accessibility} static class {className}");
        sb.AppendLine("{");

        for (var i = 0; i < models.Count; i++)
        {
            var model    = models[i];
            var modelFqn = model.FullyQualifiedName;

            // model.Name, not modelFqn: a constructed generic name would put raw angle
            // brackets in the XML and raise CS1570.
            sb.AppendLine($"    /// <summary>Validates <c>{model.Name}</c> with its generated validator whenever the options instance is resolved.</summary>");
            sb.AppendLine("    /// <param name=\"builder\">The options builder to attach validation to.</param>");
            sb.AppendLine("    /// <returns>The same options builder, so calls can be chained.</returns>");
            sb.AppendLine($"    public static global::Microsoft.Extensions.Options.OptionsBuilder<{modelFqn}> ValidateWithZeroAlloc(");
            sb.AppendLine($"        this global::Microsoft.Extensions.Options.OptionsBuilder<{modelFqn}> builder)");
            sb.AppendLine("    {");
            sb.AppendLine("        var services = builder.Services;");
            ValidatorRegistrationEmitter.AppendRegistrations(sb, [model]);
            sb.AppendLine($"        builder.Services.TryAddSingleton<global::Microsoft.Extensions.Options.IValidateOptions<{modelFqn}>,");
            sb.AppendLine($"            global::ZeroAlloc.Validation.Options.ZeroAllocOptionsValidator<{modelFqn}>>();");
            sb.AppendLine("        return builder;");
            sb.AppendLine("    }");
            if (i < models.Count - 1)
                sb.AppendLine();
        }

        for (var i = 0; i < genericModels.Count; i++)
        {
            if (models.Count > 0 || i > 0)
                sb.AppendLine();
            AppendGenericOverload(sb, genericModels[i]);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// The overload for a generic model, issue #238: generic over the model's type parameters,
    /// constrained like the model, so inference closes it from the builder it is called on. It
    /// registers the same lines as the model's <c>Add…Validator&lt;…&gt;()</c> helper. It
    /// coexists with the other overloads, since each takes a different <c>OptionsBuilder</c>.
    /// </summary>
    private static void AppendGenericOverload(StringBuilder sb, GenericModelInfo model)
    {
        var modelFqn = model.FullyQualifiedName;
        var builder  = model.Names.Builder;
        var services = model.Names.Services;

        sb.AppendLine($"    /// <summary>Validates <c>{model.Name}</c> with its generated validator whenever the options instance is resolved.</summary>");
        foreach (var parameter in model.TypeParameterNames)
            sb.AppendLine($"    /// <typeparam name=\"{parameter}\">The type argument for the <c>{parameter}</c> type parameter of <c>{model.Name}</c>.</typeparam>");
        sb.AppendLine($"    /// <param name=\"{builder}\">The options builder to attach validation to.</param>");
        sb.AppendLine("    /// <returns>The same options builder, so calls can be chained.</returns>");
        sb.AppendLine($"    public static global::Microsoft.Extensions.Options.OptionsBuilder<{modelFqn}> ValidateWithZeroAlloc{model.TypeParameterList}(");
        sb.AppendLine($"        this global::Microsoft.Extensions.Options.OptionsBuilder<{modelFqn}> {builder})");
        foreach (var clause in model.ConstraintClauses)
            sb.AppendLine($"        {clause}");
        sb.AppendLine("    {");
        sb.AppendLine($"        var {services} = {builder}.Services;");
        ValidatorRegistrationEmitter.AppendRegistrations(sb, model.Registrations);
        sb.AppendLine($"        {builder}.Services.TryAddSingleton<global::Microsoft.Extensions.Options.IValidateOptions<{modelFqn}>,");
        sb.AppendLine($"            global::ZeroAlloc.Validation.Options.ZeroAllocOptionsValidator<{modelFqn}>>();");
        sb.AppendLine($"        return {builder};");
        sb.AppendLine("    }");
    }
}
