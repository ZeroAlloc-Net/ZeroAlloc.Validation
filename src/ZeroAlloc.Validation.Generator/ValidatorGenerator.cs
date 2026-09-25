using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

[Generator]
public sealed class ValidatorGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Opaque wrapper so the IncrementalValueProvider type parameter does not reference
    /// ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo directly, which would force Roslyn
    /// to load that assembly when JIT-compiling <see cref="Initialize"/>.
    /// </summary>
    private sealed record BehaviorCache(
        System.Collections.Generic.List<ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> Sync,
        System.Collections.Generic.List<ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> Async);

    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";
    private const string ValidateWithFqn      = "ZeroAlloc.Validation.ValidateWithAttribute";
    private const string RuleMessageFqn       = "ZeroAlloc.Validation.RuleMessageAttribute";

    /// <summary>Tracking name of the ZV0026 step, so tests can assert that it stays cached.</summary>
    internal const string MisplacedRuleMessageTrackingName = "MisplacedRuleMessage";
    private const string TransientFqn = "ZeroAlloc.Inject.TransientAttribute";
    private const string ScopedFqn    = "ZeroAlloc.Inject.ScopedAttribute";
    private const string SingletonFqn = "ZeroAlloc.Inject.SingletonAttribute";

    private static readonly DiagnosticDescriptor ZV0011 = new DiagnosticDescriptor(
        id: "ZV0011",
        title: "Redundant [ValidateWith] attribute",
        messageFormat: "Property '{0}' has [ValidateWith] but its type '{1}' already has [Validate]. Remove [ValidateWith] to use the auto-generated validator.",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0012 = new DiagnosticDescriptor(
        id: "ZV0012",
        title: "Invalid [ValidateWith] validator type",
        messageFormat: "Validator type '{0}' specified via [ValidateWith] on property '{1}' does not implement ValidatorFor<{2}>",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0013 = new DiagnosticDescriptor(
        id: "ZV0013",
        title: "Invalid [CustomValidation] method signature",
        messageFormat: "Method '{0}' decorated with [CustomValidation] must have no parameters and return IEnumerable<ValidationFailure>, ValidationFailure[] or ReadOnlySpan<ValidationFailure>",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0014 = new DiagnosticDescriptor(
        id: "ZV0014",
        title: "[Validate] on non-readonly struct",
        messageFormat:
            "Struct '{0}' is decorated with [Validate] but is not declared `readonly`. " +
            "A caller can mutate the instance between the validator returning success " +
            "and the consumer reading the value, making validation results stale. " +
            "Declare the struct as `readonly struct` or `readonly record struct`.",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0015 = new DiagnosticDescriptor(
        id: "ZV0015",
        title: "Duplicate pipeline behavior Order",
        messageFormat: "Two behaviors have the same Order value {0} for model '{1}'. Each behavior must have a unique Order.",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0017 = new DiagnosticDescriptor(
        id: "ZV0017",
        title: "Validation rules depending on an inaccessible base member are ignored",
        messageFormat: "Base type member '{0}.{1}' is not accessible to the generated validator for '{2}', so the validation rules that depend on it are not enforced. Make the member public or internal, or move it to '{2}'.",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0018 = new DiagnosticDescriptor(
        id: "ZV0018",
        title: "Duplicate validation attribute",
        messageFormat: "Property '{0}' declares [{1}] more than once with the same arguments. The rule is evaluated twice and reports the same failure twice; remove the duplicate.",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0019 = new DiagnosticDescriptor(
        id: "ZV0019",
        title: "Invalid ZeroAllocGeneratedAccessibility value",
        messageFormat: "MSBuild property 'ZeroAllocGeneratedAccessibility' has invalid value '{0}'; allowed values are 'Public' and 'Internal'",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0024 = new DiagnosticDescriptor(
        id: "ZV0024",
        title: "Validation attribute applied where the generator does not read it",
        messageFormat: "'{0}' is applied to '{1}', which the generator does not validate; apply it to a property",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0025 = new DiagnosticDescriptor(
        id: "ZV0025",
        title: "[Validate] type not accessible from the generated validator",
        messageFormat: "The generated validator cannot access '{0}', so no validator is generated for it; make it and every type containing it internal or public",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0026 = new DiagnosticDescriptor(
        id: "ZV0026",
        title: "[RuleMessage] on a class that is not a custom rule",
        messageFormat: "'{0}' has [RuleMessage] but does not derive from ValidationAttribute<T>, so the message is never used",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "[RuleMessage] supplies the default message of a custom rule, an attribute deriving from "
            + "ValidationAttribute<T>. On any other class nothing reads it. Derive the class from "
            + "ValidationAttribute<T>, or remove the attribute.");

    private static readonly DiagnosticDescriptor ZV0028 = new DiagnosticDescriptor(
        id: "ZV0028",
        title: "Validation method the generated validator cannot call",
        messageFormat: "Method '{0}', used by {1}, cannot be called from the generated validator because it {2}",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The generated validator is a separate class, so it calls [CustomValidation], [Must], "
            + "When and Unless methods as instance.Method(). A static method, or one that is private "
            + "or protected, cannot be called that way, so the rule is skipped. Make the method a "
            + "public or internal instance method.");

    private static readonly DiagnosticDescriptor ZV0029 = new DiagnosticDescriptor(
        id: "ZV0029",
        title: "[Validate] on a generic type",
        messageFormat: "'{0}' is generic or declared inside a generic type, so no validator is generated for it; validate a non-generic type instead",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The generated validator is a non-generic class, so it cannot name a model that has "
            + "type parameters, or one whose containing type has them. No validator is generated, "
            + "and the Inject, Options and ASP.NET Core glue leave the type out.");

    /// <summary>
    /// ZV0026's model: a <c>[RuleMessage]</c> usage on a class that is not a custom rule. Plain
    /// strings and spans only, so the step compares by value and stays cached across edits that
    /// do not touch the class; the <see cref="Location"/> is rebuilt when the diagnostic is reported.
    /// </summary>
    private readonly record struct MisplacedRuleMessage(
        string ClassName,
        string FilePath,
        Microsoft.CodeAnalysis.Text.TextSpan Span,
        Microsoft.CodeAnalysis.Text.LinePositionSpan LineSpan)
    {
        /// <summary>
        /// A source location in the compilation's own tree, so <c>#pragma warning disable</c> and
        /// per-file severity apply to the warning; a file-path location would bypass both.
        /// </summary>
        public Location ToLocation(Compilation compilation)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (string.Equals(tree.FilePath, FilePath, StringComparison.Ordinal))
                    return Location.Create(tree, Span);
            }
            return Location.Create(FilePath, Span, LineSpan);
        }
    }

    private static readonly DiagnosticDescriptor ZV0027 = new DiagnosticDescriptor(
        id: "ZV0027",
        title: "Validation attribute applied to a property the generated validator cannot read",
        messageFormat: "'{0}' is applied to '{1}', which the generated validator cannot read because the property {2}",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // The exact MSBuild property name shared, unqualified, across every ZeroAlloc generator
    // package (issue #193). CompilerVisibleProperty in the package's build/buildTransitive
    // props makes it available here as build_property.<name>.
    private const string GeneratedAccessibilityProperty = "build_property.ZeroAllocGeneratedAccessibility";

    // The model's name for XML documentation: qualified by its containing types, so a nested
    // model reads Outer.Request, and without type arguments, whose angle brackets would raise
    // CS1570.
    private static readonly SymbolDisplayFormat DocumentationNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.None);

    private enum GeneratedAccessibilityMode { Public, Internal }

    private readonly record struct GeneratedAccessibilityResult(GeneratedAccessibilityMode Mode, Diagnostic? Diagnostic);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validateClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateAttributeFqn,
                // All four C# shapes — class, record (class), struct, record struct — hit
                // the same emission path; the downstream generator walks symbol properties
                // identically regardless of TypeKind. Note: Roslyn represents both
                // `record` and `record struct` with RecordDeclarationSyntax (kind
                // differs at the token level), so only three syntax types are needed.
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax
                         or RecordDeclarationSyntax
                         or StructDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

#pragma warning disable EPS06 // IncrementalValuesProvider<T> is a struct; Combine is the standard Roslyn API
        var behaviors = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var (sync, async_) = BehaviorDiscoverer.DiscoverAll(compilation);
                return new BehaviorCache(sync, async_);
            });
        var combined = validateClasses.Combine(behaviors);

        // ZV0019: read once per compilation, independent of whether any [Validate] class is
        // present, so an invalid value is reported even in a project with nothing to generate.
        var accessibility = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => ParseGeneratedAccessibility(provider));

        // The Compilation reaches Emit for the custom-rule checks, ZV0021's implicit-conversion
        // test and ZV0023's accessibility test. It adds no invalidation of its own: the behavior
        // cache above is already rebuilt from every new compilation.
        var combinedWithMode = combined
            .Combine(accessibility.Select(static (result, _) => result.Mode))
            .Combine(context.CompilationProvider);
#pragma warning restore EPS06

        context.RegisterSourceOutput(accessibility, static (ctx, result) =>
        {
            if (result.Diagnostic is not null)
                ctx.ReportDiagnostic(result.Diagnostic);
        });

        context.RegisterSourceOutput(combinedWithMode, static (ctx, pair) =>
            Emit(ctx, pair.Left.Left.Left, pair.Left.Left.Right, pair.Left.Right, pair.Right));

        RegisterMisplacedRuleMessage(context);
    }

    // ZV0026: [RuleMessage] is read only for custom rules. Independent of [Validate], so a
    // misplaced one is reported even in a project with nothing to generate.
    private static void RegisterMisplacedRuleMessage(IncrementalGeneratorInitializationContext context)
    {
#pragma warning disable EPS06 // IncrementalValuesProvider<T> is a struct; Where, Select and Combine are the standard Roslyn API
        var misplacedRuleMessages = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                RuleMessageFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => FindMisplacedRuleMessage(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!.Value)
            .WithTrackingName(MisplacedRuleMessageTrackingName);

        // Joined with the Compilation only after the tracked step, which therefore stays cached;
        // the Compilation serves just to turn the model back into a source location.
        var misplacedWithCompilation = misplacedRuleMessages.Combine(context.CompilationProvider);
#pragma warning restore EPS06

        context.RegisterSourceOutput(misplacedWithCompilation, static (ctx, pair) =>
            ctx.ReportDiagnostic(Diagnostic.Create(ZV0026, pair.Left.ToLocation(pair.Right), pair.Left.ClassName)));
    }

    private static MisplacedRuleMessage? FindMisplacedRuleMessage(GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type || CustomRules.TryGetRuleValueType(type, out _))
            return null;
        if (ctx.Attributes.IsDefaultOrEmpty
            || ctx.Attributes[0].ApplicationSyntaxReference?.GetSyntax(ct) is not { } syntax)
            return null;

        var location = syntax.GetLocation();
        return new MisplacedRuleMessage(
            type.Name,
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }

    // ZV0019: "Public" and "Internal" are the only allowed values, compared case-insensitively;
    // an unset or empty property defaults to Public, unchanged since before #193. Any other
    // value is an error, and every validator in the compilation falls back to Public so the
    // rest of the build still reflects today's behavior instead of silently going internal.
    private static GeneratedAccessibilityResult ParseGeneratedAccessibility(AnalyzerConfigOptionsProvider provider)
    {
        if (!provider.GlobalOptions.TryGetValue(GeneratedAccessibilityProperty, out var raw) || raw.Length == 0)
            return new GeneratedAccessibilityResult(GeneratedAccessibilityMode.Public, null);

        if (string.Equals(raw, "Public", StringComparison.OrdinalIgnoreCase))
            return new GeneratedAccessibilityResult(GeneratedAccessibilityMode.Public, null);
        if (string.Equals(raw, "Internal", StringComparison.OrdinalIgnoreCase))
            return new GeneratedAccessibilityResult(GeneratedAccessibilityMode.Internal, null);

        var diagnostic = Diagnostic.Create(ZV0019, Location.None, raw);
        return new GeneratedAccessibilityResult(GeneratedAccessibilityMode.Public, diagnostic);
    }

    private static void Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol, BehaviorCache allBehaviors, GeneratedAccessibilityMode mode, Compilation compilation)
    {
        if (ReportModelWithoutValidator(ctx, classSymbol, compilation))
            return;

        // ZV0014 — surface mutability hazard on non-readonly structs. Generator
        // still proceeds to emit the validator; the warning is informational.
        if (classSymbol.TypeKind == TypeKind.Struct && !classSymbol.IsReadOnly)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                ZV0014,
                classSymbol.Locations.FirstOrDefault() ?? Location.None,
                classSymbol.Name));
        }

        var modelFqn = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var (syncBehaviors, asyncBehaviors) = BehaviorDiscoverer.ForModel(allBehaviors.Sync, allBehaviors.Async, modelFqn);

        ReportNestedDiagnostics(ctx, classSymbol, compilation);
        ReportDuplicateOrderDiagnostics(ctx, syncBehaviors, asyncBehaviors, classSymbol.Name);

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        // The validator is a top-level class in the model's namespace, so it names the model by
        // its fully qualified name: a model nested in another type is not in scope by its simple
        // name there, issue #207. GeneratedValidatorNames documents the validator's own name.
        var validatorName = GeneratedValidatorNames.ValidatorName(classSymbol);
        var modelName = modelFqn;

        var sb = new System.Text.StringBuilder();
        EmitFileHeader(sb, namespaceName, classSymbol, validatorName, modelName, mode, compilation);

        var nestedFields = RuleEmitter.CollectNestedValidatorFields(classSymbol, compilation);
        EmitFieldsAndConstructor(sb, validatorName, nestedFields);

        // 1.5.3: shared collector so both sync (Validate) and async (ValidateAsync)
        // emission paths populate the same set of static fields ([Matches] regexes and
        // user-defined rule instances). Emitting the declarations once after both paths
        // run guarantees each field appears exactly once at class scope
        // (no CS0102 duplicate-member errors when both paths reference the same prop).
        var fields = new GeneratedFields();
        EmitValidateMethod(ctx, sb, classSymbol, compilation, modelName, syncBehaviors, fields);
        EmitValidateAsyncOverride(sb, classSymbol, compilation, modelName, asyncBehaviors, fields);

        EmitMatchesRegexFields(sb, fields);
        EmitRuleInstanceFields(sb, fields);

        sb.AppendLine("}");

        ctx.AddSource(GeneratedValidatorNames.HintName(classSymbol), sb.ToString());
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
    private static void EmitMatchesRegexFields(
        System.Text.StringBuilder sb,
        GeneratedFields fields)
    {
        foreach (var kvp in fields.RegexPatterns)
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
    private static void EmitRuleInstanceFields(
        System.Text.StringBuilder sb,
        GeneratedFields fields)
    {
        foreach (var kvp in fields.RuleInstances)
        {
            sb.AppendLine();
            if (kvp.Value.IsObsolete)
            {
                sb.AppendLine("    // The rule type is obsolete; the compiler already warns at the attribute usage in user code.");
                sb.AppendLine("#pragma warning disable CS0618, CS0612");
            }
            sb.AppendLine($"    private static readonly {kvp.Value.TypeName} {kvp.Key}");
            sb.AppendLine($"        = {kvp.Value.Initializer};");
            if (kvp.Value.IsObsolete)
                sb.AppendLine("#pragma warning restore CS0618, CS0612");
        }
    }

    private static void EmitValidateMethod(
        SourceProductionContext ctx,
        System.Text.StringBuilder sb,
        INamedTypeSymbol classSymbol,
        Compilation compilation,
        string modelName,
        List<global::ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> syncBehaviors,
        GeneratedFields fields)
    {
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine($"    public override global::ZeroAlloc.Validation.ValidationResult Validate({modelName} instance)");
        sb.AppendLine("    {");
        if (syncBehaviors.Count == 0)
        {
            RuleEmitter.EmitValidateBody(sb, classSymbol, compilation, "instance", ctx, fields);
        }
        else
        {
            var fullyQualifiedModel = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var capturedCtx = ctx;
            var syncShape = new global::ZeroAlloc.Pipeline.Generators.PipelineShape
            {
                TypeArguments           = new[] { fullyQualifiedModel },
                OuterParameterNames     = new[] { "instance" },
                LambdaParameterPrefixes = new[] { "r" },
                InnermostBodyFactory    = depth =>
                {
                    var paramName = depth == 0 ? "instance" : $"r{depth}";
                    return "{\n"
                        + RuleEmitter.EmitValidateBodyAsString(classSymbol, compilation, paramName, capturedCtx, fields)
                        + "        }";
                }
            };
            var chain = global::ZeroAlloc.Pipeline.Generators.PipelineEmitter.EmitChain(syncBehaviors, syncShape);
            sb.AppendLine($"        return {chain};");
        }
        sb.AppendLine("    }");
    }

    private static void EmitValidateAsyncOverride(
        System.Text.StringBuilder sb,
        INamedTypeSymbol classSymbol,
        Compilation compilation,
        string modelName,
        List<global::ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> asyncBehaviors,
        GeneratedFields fields)
    {
        if (asyncBehaviors.Count == 0)
            return;

        var fullyQualifiedModel = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var asyncShape = new global::ZeroAlloc.Pipeline.Generators.PipelineShape
        {
            TypeArguments           = new[] { fullyQualifiedModel },
            OuterParameterNames     = new[] { "instance", "ct" },
            LambdaParameterPrefixes = new[] { "r", "c" },
            InnermostBodyFactory    = depth =>
            {
                var paramName = depth == 0 ? "instance" : $"r{depth}";
                // Inline the full validation logic, but each `return new ValidationResult(...);\n`
                // must be wrapped in ValueTask.FromResult. Replace the terminal fragment.
                const string returnPrefix = "return new global::ZeroAlloc.Validation.ValidationResult(";
                const string wrapOpen     = "return global::System.Threading.Tasks.ValueTask.FromResult(new global::ZeroAlloc.Validation.ValidationResult(";
                // wrapClose adds one extra closing paren for FromResult(...) and the semicolon.
                // The closing paren for ValidationResult(...) is already included in the matched substring.
                const string wrapClose    = ");";
                var body = RuleEmitter.EmitValidateBodyAsString(classSymbol, compilation, paramName, fields: fields);
                var asyncBody = WrapReturnSites(body, returnPrefix, wrapOpen, wrapClose);
                return "{\n" + asyncBody + "        }";
            }
        };
        var chain = global::ZeroAlloc.Pipeline.Generators.PipelineEmitter.EmitChain(asyncBehaviors, asyncShape);
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine($"    public override global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult> ValidateAsync({modelName} instance, global::System.Threading.CancellationToken ct = default)");
        sb.AppendLine($"        => {chain};");
    }

    /// <summary>
    /// Replaces every <c>return new ValidationResult(...);</c> pattern in the body by
    /// wrapping the whole construct in <c>ValueTask.FromResult(…)</c>.
    /// </summary>
    private static string WrapReturnSites(string body, string returnPrefix, string wrapOpen, string wrapClose)
    {
        var result = new System.Text.StringBuilder(body.Length + 128);
        int pos = 0;
        while (pos < body.Length)
        {
            int start = body.IndexOf(returnPrefix, pos, StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(body, pos, body.Length - pos);
                break;
            }
            result.Append(body, pos, start - pos);
            result.Append(wrapOpen);
            // Find the matching semicolon that ends the return statement.
            // The return statement is: return new ValidationResult(...);
            // We need to find the ';' that terminates it (accounting for nested parens).
            int valueStart = start + returnPrefix.Length;
            int depth2 = 1; // one open paren from returnPrefix
            int i = valueStart;
            while (i < body.Length && depth2 > 0)
            {
                if (body[i] == '(') depth2++;
                else if (body[i] == ')') depth2--;
                i++;
            }
            // i now points just past the closing ')'; body[i] should be ';'
            result.Append(body, valueStart, i - valueStart); // includes final ')'
            result.Append(wrapClose);  // closes ValueTask.FromResult( — ValidationResult's ')' was already in matched
            pos = i + 1; // skip original ';'
        }
        // Also wrap the nested-path terminal (emitted by EmitNestedPath / collection paths)
        result.Replace(
            "return _buf.ToResult();",
            "return global::System.Threading.Tasks.ValueTask.FromResult(_buf.ToResult());");
        return result.ToString();
    }

    private static void EmitFileHeader(
        System.Text.StringBuilder sb,
        string? namespaceName,
        INamedTypeSymbol classSymbol,
        string validatorName,
        string modelName,
        GeneratedAccessibilityMode mode,
        Compilation compilation)
    {
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable HLQ004 // ref readonly foreach on ReadOnlySpan<struct> — intentional, avoids struct copy");
        sb.AppendLine("#pragma warning disable EPS06  // False positive: ValidationFailure is a readonly struct");
        sb.AppendLine();
        sb.AppendLine("using ZeroAlloc.Validation;");
        sb.AppendLine();

        if (namespaceName is not null)
        {
            sb.AppendLine($"namespace {namespaceName};");
            sb.AppendLine();
        }

        var lifetimeFqn = classSymbol.GetAttributes()
            .Select(a => a.AttributeClass?.ToDisplayString())
            .FirstOrDefault(fqn => fqn is TransientFqn or ScopedFqn or SingletonFqn);

        if (lifetimeFqn is not null)
            sb.AppendLine($"[global::{lifetimeFqn}]");

        // Every publicly visible generated member carries documentation. The <auto-generated>
        // header suppresses analyzer diagnostics but not compiler ones, so an undocumented
        // public member raises CS1591 in any consumer with GenerateDocumentationFile enabled.
        // Documenting rather than suppressing also puts these members in the consumer's own
        // XML documentation file, which a #pragma would not.
        sb.AppendLine($"/// <summary>Validates <c>{classSymbol.ToDisplayString(DocumentationNameFormat)}</c> instances against the rules declared on the type.</summary>");
        // The validator follows the model's effective accessibility. A public validator over an
        // internal model fails with CS9338 and CS0051, issue #184. Extended for issue #193
        // point 3: the validator is public only if the model itself is effectively public AND
        // every nested [Validate] model it takes as a constructor-injected validator dependency
        // would itself resolve to a public validator, computed transitively — otherwise the
        // outer validator's public constructor would take a less-accessible parameter and fail
        // with CS0051, exactly the case point 3 reported. With
        // ZeroAllocGeneratedAccessibility=Internal, every validator is internal regardless.
        var accessibility = mode == GeneratedAccessibilityMode.Public && NestedValidatorAccessibility.WouldBePublic(classSymbol, compilation)
            ? "public"
            : "internal";
        sb.AppendLine($"{accessibility} sealed partial class {validatorName} : ValidatorFor<{modelName}>");
        sb.AppendLine("{");
    }

    private static void EmitFieldsAndConstructor(
        System.Text.StringBuilder sb,
        string validatorName,
        List<(string FieldName, string ParamName, string QualifiedValidatorType)> nestedFields)
    {
        if (nestedFields.Count == 0)
            return;

        foreach (var (fieldName, _, qualifiedType) in nestedFields)
            sb.AppendLine($"    private readonly {qualifiedType} {fieldName};");

        sb.AppendLine();

        sb.AppendLine($"    /// <summary>Initialises a new <c>{validatorName}</c> with the validators for its nested members.</summary>");
        // Parameter types are not named in the text: a constructed generic such as
        // ValidatorFor<Foo> would put raw angle brackets in the XML and raise CS1570.
        // The member name is recovered from the parameter name instead, which the caller
        // built as <member>Validator.
        foreach (var (_, paramName, _) in nestedFields)
            sb.AppendLine($"    /// <param name=\"{paramName}\">The validator for the nested <c>{DescribeNestedMember(paramName)}</c> member.</param>");

        sb.Append($"    public {validatorName}(");
        for (int fi = 0; fi < nestedFields.Count; fi++)
        {
            var (_, paramName, qualifiedType) = nestedFields[fi];
            if (fi > 0) sb.Append(", ");
            sb.Append($"{qualifiedType} {paramName}");
        }
        sb.AppendLine(")");
        sb.AppendLine("    {");
        foreach (var (fieldName, paramName, _) in nestedFields)
            sb.AppendLine($"        {fieldName} = {paramName};");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>
    /// Recovers the nested property's name from the constructor parameter name for use in
    /// documentation. <c>CollectNestedValidatorFields</c> builds the parameter as the
    /// camel-cased property name followed by <c>Validator</c>, so removing one such suffix
    /// and restoring the leading capital yields the original property name.
    /// </summary>
    private static string DescribeNestedMember(string paramName)
    {
        const string suffix = "Validator";

        var trimmed = paramName.EndsWith(suffix, StringComparison.Ordinal)
            ? paramName.Substring(0, paramName.Length - suffix.Length)
            : paramName;

        // Defensive: a property named exactly "Validator" would trim to nothing.
        if (trimmed.Length == 0)
            return paramName;

        return $"{char.ToUpperInvariant(trimmed[0])}{trimmed.Substring(1)}";
    }

    private static void ReportDuplicateOrderDiagnostics(
        SourceProductionContext ctx,
        List<global::ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> sync,
        List<global::ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> async_,
        string modelName)
    {
        var all = new List<global::ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo>(sync.Count + async_.Count);
        all.AddRange(sync);
        all.AddRange(async_);

        var seen = new System.Collections.Generic.Dictionary<int, string>();
        for (int i = 0; i < all.Count; i++)
        {
            var b = all[i];
            if (seen.TryGetValue(b.Order, out _))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    ZV0015,
                    Location.None,
                    b.Order.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    modelName));
            }
            else
            {
                seen[b.Order] = b.BehaviorTypeName;
            }
        }
    }

    private static void ReportNestedDiagnostics(SourceProductionContext ctx, INamedTypeSymbol classSymbol, Compilation compilation)
    {
        foreach (var member in MemberWalker.GetMembersIncludingBase(classSymbol))
        {
            if (member is not IPropertySymbol prop) continue;

            var validateWithAttr = FindValidateWithAttribute(prop);
            if (validateWithAttr is null) continue;

            ReportZV0011IfApplicable(ctx, prop, member, validateWithAttr, compilation);
            ReportZV0012IfApplicable(ctx, prop, member, validateWithAttr);
        }
        ReportCustomValidationDiagnostics(ctx, classSymbol, compilation);
        ReportInaccessibleBaseMemberDiagnostics(ctx, classSymbol, compilation);
        ReportDuplicateRuleAttributeDiagnostics(ctx, classSymbol);
        ReportUnreadValidationAttributeDiagnostics(ctx, classSymbol, compilation);
    }

    /// <summary>
    /// ZV0024: the generator reads rules from properties only. A <c>ValidationAttribute</c>
    /// subclass can widen its own <c>[AttributeUsage]</c>, so a rule can compile on a field, or
    /// on a constructor parameter such as a record's positional parameter written without the
    /// <c>property:</c> target, and would then be dropped with nothing to say so. Fields and
    /// constructor parameters are searched on <paramref name="classSymbol"/> and on each base
    /// type whose properties it validates. The walk stops at a base type that is itself
    /// <c>[Validate]</c>, whose own generation reports its members, so each usage reports once.
    /// The same walk reports ZV0027 for the properties the validator cannot read, on every type
    /// whose rules <paramref name="classSymbol"/>'s validator inherits. It skips a property a
    /// more-derived declaration hides, a base type that is not declared in source, which the user
    /// cannot change and which has no location to report at, and a base type whose usages the
    /// generation of a <c>[Validate]</c> base reports, as
    /// <see cref="MethodReachability.IsReportedByBaseValidator"/> decides for ZV0017 and ZV0028.
    /// A generic <c>[Validate]</c> base type gets no validator, ZV0029, so it reports nothing and
    /// neither walk stops there.
    /// </summary>
    private static void ReportUnreadValidationAttributeDiagnostics(SourceProductionContext ctx, INamedTypeSymbol classSymbol, Compilation compilation)
    {
        var includeBase = MemberWalker.IncludesBaseProperties(classSymbol);
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        bool reportUnread = true;
        for (var type = classSymbol; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            bool isBase = !SymbolEqualityComparer.Default.Equals(type, classSymbol);
            if (isBase && !includeBase)
                break;

            // ZV0024 stops at the first [Validate] base type, as it always has; see #229.
            if (isBase && HasValidateAttribute(type) && GeneratedValidatorReach.HasGeneratedValidator(type, compilation))
                reportUnread = false;

            bool reportUnreadable = type.Locations.Any(l => l.IsInSource)
                && !(isBase && MethodReachability.IsReportedByBaseValidator(compilation, classSymbol, type));

            foreach (var member in type.GetMembers())
            {
                switch (member)
                {
                    case IPropertySymbol property:
                        if (reportUnreadable && !hidden.Contains(property.Name))
                            ReportUnreadablePropertyAttributes(ctx, property, classSymbol, isBase);
                        break;
                    case IFieldSymbol field when reportUnread:
                        ReportUnreadValidationAttributes(ctx, field, field.Name);
                        break;
                    case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor when reportUnread:
                        foreach (var parameter in constructor.Parameters)
                            ReportUnreadValidationAttributes(ctx, parameter, parameter.Name);
                        break;
                }
            }

            foreach (var member in type.GetMembers())
            {
                if (MemberWalker.HidesBaseMembers(member, classSymbol))
                    hidden.Add(member.Name);
            }
        }
    }

    private static void ReportUnreadValidationAttributes(SourceProductionContext ctx, ISymbol target, string targetName)
    {
        foreach (var attr in target.GetAttributes())
        {
            if (attr.AttributeClass is not { } attrClass || !CustomRules.DerivesFromValidationAttribute(attrClass))
                continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                ZV0024,
                attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? target.Locations.FirstOrDefault(l => l.IsInSource)
                    ?? Location.None,
                attrClass.Name,
                targetName));
        }
    }

    /// <summary>
    /// Reports a model that gets no validator. ZV0025: the validator is a top-level class in its
    /// own file, so it cannot name a private, protected or private protected nested type, a type
    /// inside one, or a file-local type. ZV0029: the validator is not generic, so it cannot name a
    /// model that is generic or declared inside a generic type, issue #219. A model can hit both,
    /// and each names a change it needs, so both are reported rather than one hiding the other.
    /// Every other diagnostic describes how the validator would treat the type's rules; with no
    /// validator they are moot, and they run as usual once the type gets one. The companion
    /// generators leave the type out through the same checks, so these errors are the only ones
    /// the user sees for it.
    /// </summary>
    /// <returns>Whether the model was reported, in which case nothing is generated for it.</returns>
    private static bool ReportModelWithoutValidator(SourceProductionContext ctx, INamedTypeSymbol classSymbol, Compilation compilation)
    {
        var reported = false;
        if (!GeneratedValidatorReach.CanReach(classSymbol, compilation))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                ZV0025,
                FindValidateAttributeLocation(classSymbol),
                classSymbol.ToDisplayString()));
            reported = true;
        }

        if (GeneratedValidatorReach.IsGeneric(classSymbol))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                ZV0029,
                FindValidateAttributeLocation(classSymbol),
                classSymbol.ToDisplayString()));
            reported = true;
        }

        return reported;
    }

    private static Location FindValidateAttributeLocation(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, StringComparison.Ordinal)
                && attr.ApplicationSyntaxReference is { } reference)
                return reference.GetSyntax().GetLocation();
        }
        return type.Locations.FirstOrDefault() ?? Location.None;
    }

    /// <summary>
    /// ZV0027: the generated validator reads a property as <c>instance.Prop</c>, which does not
    /// compile for a static property, an indexer, a property with no getter, or one whose getter
    /// the validator cannot access. <see cref="MemberWalker.GetMembersIncludingBase"/> leaves such
    /// a property out, and each rule or <c>[ValidateWith]</c> placed on it is reported here. On a
    /// base type, a property that is only inaccessible is ZV0017's case and is not reported again.
    /// </summary>
    private static void ReportUnreadablePropertyAttributes(
        SourceProductionContext ctx, IPropertySymbol property, INamedTypeSymbol classSymbol, bool isBase)
    {
        var reason = MemberWalker.GetUnreadableReason(property, classSymbol);
        if (reason == UnreadableReason.None) return;
        if (isBase && !MemberWalker.IsUnreadableByShape(reason)) return;

        foreach (var attr in property.GetAttributes())
        {
            if (attr.AttributeClass is not { } attrClass) continue;
            if (!CustomRules.DerivesFromValidationAttribute(attrClass)
                && !string.Equals(attrClass.ToDisplayString(), ValidateWithFqn, StringComparison.Ordinal))
                continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                ZV0027,
                attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? property.Locations.FirstOrDefault(l => l.IsInSource)
                    ?? Location.None,
                attrClass.Name,
                property.Name,
                DescribeUnreadableReason(reason)));
        }
    }

    private static string DescribeUnreadableReason(UnreadableReason reason) => reason switch
    {
        UnreadableReason.Static => "is static",
        UnreadableReason.Indexer => "is an indexer",
        UnreadableReason.NoGetter => "has no get accessor",
        UnreadableReason.Inaccessible => "is not accessible from the generated validator",
        _ => "has a get accessor the generated validator cannot access",
    };

    private static bool HasValidateAttribute(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Rule attributes are <c>AllowMultiple</c>, which they have to be — <c>[Must(nameof(A))]</c>
    /// alongside <c>[Must(nameof(B))]</c> is meaningful, and so is the same check with different
    /// arguments. Repeating one with *identical* arguments is not: the rule runs twice and the
    /// same failure is reported twice. Only that exact-duplicate case is reported.
    /// </summary>
    private static void ReportDuplicateRuleAttributeDiagnostics(SourceProductionContext ctx, INamedTypeSymbol classSymbol)
    {
        foreach (var member in MemberWalker.GetMembersIncludingBase(classSymbol))
        {
            if (member is not IPropertySymbol prop) continue;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attr in prop.GetAttributes())
            {
                var ns = attr.AttributeClass?.ContainingNamespace?.ToDisplayString();
                if (!string.Equals(ns, "ZeroAlloc.Validation", StringComparison.Ordinal)
                    && !CustomRules.IsCustomRule(attr)) continue;

                if (seen.Add(DescribeAttribute(attr))) continue;

                ctx.ReportDiagnostic(Diagnostic.Create(
                    ZV0018,
                    attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                        ?? prop.Locations.FirstOrDefault(),
                    prop.Name,
                    attr.AttributeClass?.Name));
            }
        }
    }

    /// <summary>
    /// An attribute's identity for duplicate detection: its type plus every constructor and named
    /// argument, so the same check with different arguments is not mistaken for a repeat.
    /// </summary>
    private static string DescribeAttribute(AttributeData attr)
    {
        var sb = new System.Text.StringBuilder(attr.AttributeClass?.ToDisplayString());

        sb.Append('(');
        foreach (var arg in attr.ConstructorArguments)
            sb.Append(Describe(arg)).Append(',');
        sb.Append(')');

        // Named arguments are order-independent in source, so sort before comparing.
        var named = new List<string>();
        foreach (var arg in attr.NamedArguments)
            named.Add(arg.Key + "=" + Describe(arg.Value));
        named.Sort(StringComparer.Ordinal);

        for (int i = 0; i < named.Count; i++)
            sb.Append(named[i]).Append(';');

        return sb.ToString();
    }

    private static string Describe(TypedConstant value)
    {
        if (value.Kind == TypedConstantKind.Array)
        {
            var sb = new System.Text.StringBuilder("[");
            foreach (var element in value.Values)
                sb.Append(Describe(element)).Append(',');
            return sb.Append(']').ToString();
        }

        return value.IsNull
            ? "null"
            : System.Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "null";
    }

    /// <summary>
    /// ZV0017 for base members the generated validator cannot reach, and ZV0017 or ZV0028 for the
    /// <c>When</c>, <c>Unless</c> and <c>[Must]</c> methods rules call. A static method is ZV0028
    /// wherever it is declared, since widening it would not make it callable. An inaccessible
    /// instance method is ZV0028 on the model and ZV0017 on a base type, which may not be the
    /// author's to change. A usage declared on a <c>[Validate]</c> base type is left to that
    /// type's own generation, so it is reported once and under one ID.
    /// </summary>
    private static void ReportInaccessibleBaseMemberDiagnostics(SourceProductionContext ctx, INamedTypeSymbol classSymbol, Compilation compilation)
    {
        foreach (var member in MemberWalker.GetInaccessibleBaseMembers(classSymbol))
        {
            // A [Validate] base type that walks the member's declaring type reports it, as ZV0027
            // for its own properties or ZV0028 for its own methods.
            if (MethodReachability.IsReportedByBaseValidator(compilation, classSymbol, member.ContainingType))
                continue;

            // A static [CustomValidation] method is reported by ReportCustomValidationDiagnostics
            // as ZV0013 or ZV0028.
            if (member is IMethodSymbol { IsStatic: true })
                continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                ZV0017,
                member.Locations.FirstOrDefault(),
                member.ContainingType?.Name,
                member.Name,
                classSymbol.Name));
        }

        // A reachable property can still carry a rule that calls a method the validator cannot.
        foreach (var member in MemberWalker.GetMembersIncludingBase(classSymbol))
        {
            if (member is not IPropertySymbol prop) continue;
            if (MethodReachability.IsReportedByBaseValidator(compilation, classSymbol, prop.ContainingType)) continue;

            foreach (var call in RuleEmitter.GetUnreachableMethodCalls(compilation, classSymbol, prop))
            {
                if (call.Reach == MethodReach.InaccessibleOnBase)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        ZV0017,
                        prop.Locations.FirstOrDefault(),
                        call.Method?.ContainingType?.Name,
                        call.MethodName,
                        classSymbol.Name));
                }
                else
                {
                    ReportZV0028(ctx, call.Attribute, prop, call.MethodName, call.Usage, call.Reach);
                }
            }
        }
    }

    /// <summary>
    /// ZV0013 for a <c>[CustomValidation]</c> method whose signature the validator cannot use, and
    /// otherwise ZV0028 when the method is static or, on the model itself, inaccessible. The
    /// signature is checked first, so a method gets one diagnostic. An inaccessible instance
    /// method on a base type is ZV0017's case, reported by
    /// <see cref="ReportInaccessibleBaseMemberDiagnostics"/>.
    /// </summary>
    private static void ReportCustomValidationDiagnostics(SourceProductionContext ctx, INamedTypeSymbol classSymbol, Compilation compilation)
    {
        const string customValidationFqn = "ZeroAlloc.Validation.CustomValidationAttribute";

        // GetMembersIncludingBase leaves out inaccessible base members; the static ones among
        // them are reported here rather than as ZV0017.
        var candidates = MemberWalker.GetMembersIncludingBase(classSymbol)
            .Concat(MemberWalker.GetInaccessibleBaseMembers(classSymbol).Where(m => m is IMethodSymbol { IsStatic: true }));

        foreach (var member in candidates)
        {
            if (member is not IMethodSymbol method) continue;

            AttributeData? attrData = null;
            foreach (var attr in method.GetAttributes())
            {
                if (string.Equals(attr.AttributeClass?.ToDisplayString(), customValidationFqn, StringComparison.Ordinal))
                {
                    attrData = attr;
                    break;
                }
            }
            if (attrData is null) continue;

            bool validSignature = method.Parameters.Length == 0
                && IsSupportedCustomValidationReturnType(method.ReturnType);

            if (!validSignature)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(ZV0013,
                    attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                        ?? member.Locations.FirstOrDefault(),
                    method.Name));
                continue;
            }

            var reach = MethodReachability.Classify(compilation, classSymbol, method);
            if (reach is MethodReach.Static or MethodReach.Inaccessible
                && !MethodReachability.IsReportedByBaseValidator(compilation, classSymbol, method.ContainingType))
            {
                ReportZV0028(ctx, attrData, method, method.Name, "[CustomValidation]", reach);
            }
        }
    }

    /// <summary>
    /// ZV0028: a <c>[CustomValidation]</c>, <c>[Must]</c>, <c>When</c> or <c>Unless</c> method the
    /// generated validator cannot call as <c>instance.Method()</c>. Reported at the attribute; the
    /// rule has already been left out of the generated code.
    /// </summary>
    private static void ReportZV0028(
        SourceProductionContext ctx, AttributeData attr, ISymbol target, string methodName, string usage, MethodReach reach)
    {
        ctx.ReportDiagnostic(Diagnostic.Create(
            ZV0028,
            attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? target.Locations.FirstOrDefault(l => l.IsInSource)
                ?? Location.None,
            methodName,
            usage,
            reach == MethodReach.Static ? "is static" : "is not accessible from it"));
    }

    /// <summary>
    /// Return types the generated validator can walk. <c>IEnumerable&lt;ValidationFailure&gt;</c>
    /// remains supported, but a method written with <c>yield</c> allocates its iterator state
    /// machine on every call even when it yields nothing, so an array or a span is accepted as the
    /// allocation-free alternative.
    /// </summary>
    private static bool IsSupportedCustomValidationReturnType(ITypeSymbol returnType)
    {
        var display = returnType.ToDisplayString();
        return string.Equals(display, "System.Collections.Generic.IEnumerable<ZeroAlloc.Validation.ValidationFailure>", StringComparison.Ordinal)
            || string.Equals(display, "ZeroAlloc.Validation.ValidationFailure[]", StringComparison.Ordinal)
            || string.Equals(display, "System.ReadOnlySpan<ZeroAlloc.Validation.ValidationFailure>", StringComparison.Ordinal);
    }

    private static AttributeData? FindValidateWithAttribute(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(), ValidateWithFqn, StringComparison.Ordinal))
                return attr;
        }
        return null;
    }

    /// <summary>
    /// ZV0011: <c>[ValidateWith]</c> on a property whose type already gets a generated validator.
    /// A <c>[Validate]</c> type that gets none, ZV0025 or ZV0029, is not reported: there is no
    /// auto-generated validator to switch to, so <c>[ValidateWith]</c> is the way to validate it.
    /// </summary>
    private static void ReportZV0011IfApplicable(
        SourceProductionContext ctx,
        IPropertySymbol prop,
        ISymbol member,
        AttributeData validateWithAttr,
        Compilation compilation)
    {
        if (prop.Type is not INamedTypeSymbol propNamed) return;
        if (!GeneratedValidatorReach.HasGeneratedValidator(propNamed, compilation)) return;

        foreach (var a in propNamed.GetAttributes())
        {
            if (string.Equals(a.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, StringComparison.Ordinal))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(ZV0011,
                    validateWithAttr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                        ?? member.Locations.FirstOrDefault(),
                    prop.Name, prop.Type.Name));
                return;
            }
        }
    }

    private static void ReportZV0012IfApplicable(
        SourceProductionContext ctx,
        IPropertySymbol prop,
        ISymbol member,
        AttributeData validateWithAttr)
    {
        var specifiedType = validateWithAttr.ConstructorArguments.Length > 0
            ? validateWithAttr.ConstructorArguments[0].Value as INamedTypeSymbol
            : null;

        if (specifiedType is null) return;

        ITypeSymbol expectedModelType = RuleEmitter.GetCollectionElementTypePublic(prop) ?? prop.Type;

        if (!ImplementsValidatorFor(specifiedType, expectedModelType))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(ZV0012,
                validateWithAttr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? member.Locations.FirstOrDefault(),
                specifiedType.Name, prop.Name, expectedModelType.Name));
        }
    }

    private static bool ImplementsValidatorFor(INamedTypeSymbol specifiedType, ITypeSymbol expectedModelType)
    {
        var current = specifiedType.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType
                && string.Equals(current.OriginalDefinition.ToDisplayString(),
                    "ZeroAlloc.Validation.ValidatorFor<T>", StringComparison.Ordinal)
                && current.TypeArguments.Length == 1
                && SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], expectedModelType))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }
}
