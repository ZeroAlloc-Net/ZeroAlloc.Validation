using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Validation.Generator;

[Generator]
public sealed class ValidatorGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Opaque wrapper so the IncrementalValueProvider type parameter does not reference
    /// ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo directly, which would force Roslyn
    /// to load that assembly when JIT-compiling <see cref="Initialize"/>.
    /// </summary>
    private sealed class BehaviorCache
    {
        public System.Collections.Generic.List<ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> Sync  { get; }
        public System.Collections.Generic.List<ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> Async { get; }
        public BehaviorCache(
            System.Collections.Generic.List<ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> sync,
            System.Collections.Generic.List<ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> async_)
        { Sync = sync; Async = async_; }
    }

    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";
    private const string ValidateWithFqn      = "ZeroAlloc.Validation.ValidateWithAttribute";
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
        messageFormat: "Method '{0}' decorated with [CustomValidation] must have no parameters and return IEnumerable<ValidationFailure>",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0015 = new DiagnosticDescriptor(
        id: "ZV0015",
        title: "Duplicate pipeline behavior Order",
        messageFormat: "Two behaviors have the same Order value {0} for model '{1}'. Each behavior must have a unique Order.",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validateClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

#pragma warning disable EPS06 // IncrementalValuesProvider<T> is a struct; Combine is the standard Roslyn API
        var behaviors = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var (sync, async_) = BehaviorDiscoverer.DiscoverAll(compilation);
                return new BehaviorCache(sync, async_);
            });
        var combined = validateClasses.Combine(behaviors);
#pragma warning restore EPS06
        context.RegisterSourceOutput(combined, static (ctx, pair) => Emit(ctx, pair.Left, pair.Right));
    }

    private static void Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol, BehaviorCache allBehaviors)
    {
        var modelFqn = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var (syncBehaviors, asyncBehaviors) = BehaviorDiscoverer.ForModel(allBehaviors.Sync, allBehaviors.Async, modelFqn);

        if (classSymbol.DeclaredAccessibility == Accessibility.Private)
            return;

        ReportNestedDiagnostics(ctx, classSymbol);
        ReportDuplicateOrderDiagnostics(ctx, syncBehaviors, asyncBehaviors, classSymbol.Name);

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        var validatorName = $"{classSymbol.Name}Validator";
        var modelName = classSymbol.Name;

        var sb = new System.Text.StringBuilder();
        EmitFileHeader(sb, namespaceName, classSymbol, validatorName, modelName);

        var nestedFields = RuleEmitter.CollectNestedValidatorFields(classSymbol);
        EmitFieldsAndConstructor(sb, validatorName, nestedFields);

        EmitValidateMethod(sb, classSymbol, modelName, syncBehaviors);
        EmitValidateAsyncOverride(sb, classSymbol, modelName, asyncBehaviors);

        sb.AppendLine("}");

        ctx.AddSource($"{validatorName}.g.cs", sb.ToString());
    }

    private static void EmitValidateMethod(
        System.Text.StringBuilder sb,
        INamedTypeSymbol classSymbol,
        string modelName,
        List<global::ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> syncBehaviors)
    {
        sb.AppendLine($"    public override global::ZeroAlloc.Validation.ValidationResult Validate({modelName} instance)");
        sb.AppendLine("    {");
        if (syncBehaviors.Count == 0)
        {
            RuleEmitter.EmitValidateBody(sb, classSymbol);
        }
        else
        {
            var fullyQualifiedModel = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var syncShape = new global::ZeroAlloc.Pipeline.Generators.PipelineShape
            {
                TypeArguments           = new[] { fullyQualifiedModel },
                OuterParameterNames     = new[] { "instance" },
                LambdaParameterPrefixes = new[] { "r" },
                InnermostBodyFactory    = depth =>
                {
                    var paramName = depth == 0 ? "instance" : $"r{depth}";
                    return "{\n"
                        + RuleEmitter.EmitValidateBodyAsString(classSymbol, paramName)
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
        string modelName,
        List<global::ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo> asyncBehaviors)
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
                var body = RuleEmitter.EmitValidateBodyAsString(classSymbol, paramName);
                var asyncBody = WrapReturnSites(body, returnPrefix, wrapOpen, wrapClose);
                return "{\n" + asyncBody + "        }";
            }
        };
        var chain = global::ZeroAlloc.Pipeline.Generators.PipelineEmitter.EmitChain(asyncBehaviors, asyncShape);
        sb.AppendLine();
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
        return result.ToString();
    }

    private static void EmitFileHeader(
        System.Text.StringBuilder sb,
        string? namespaceName,
        INamedTypeSymbol classSymbol,
        string validatorName,
        string modelName)
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

        sb.AppendLine($"public sealed partial class {validatorName} : ValidatorFor<{modelName}>");
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

    private static void ReportNestedDiagnostics(SourceProductionContext ctx, INamedTypeSymbol classSymbol)
    {
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;

            var validateWithAttr = FindValidateWithAttribute(prop);
            if (validateWithAttr is null) continue;

            ReportZV0011IfApplicable(ctx, prop, member, validateWithAttr);
            ReportZV0012IfApplicable(ctx, prop, member, validateWithAttr);
        }
        ReportCustomValidationDiagnostics(ctx, classSymbol);
    }

    private static void ReportCustomValidationDiagnostics(SourceProductionContext ctx, INamedTypeSymbol classSymbol)
    {
        const string customValidationFqn = "ZeroAlloc.Validation.CustomValidationAttribute";
        const string expectedReturnType = "System.Collections.Generic.IEnumerable<ZeroAlloc.Validation.ValidationFailure>";

        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IMethodSymbol method) continue;

            bool hasAttr = false;
            AttributeData? attrData = null;
            foreach (var attr in method.GetAttributes())
            {
                if (string.Equals(attr.AttributeClass?.ToDisplayString(), customValidationFqn, StringComparison.Ordinal))
                {
                    hasAttr = true;
                    attrData = attr;
                    break;
                }
            }
            if (!hasAttr) continue;

            bool validSignature = method.Parameters.Length == 0
                && string.Equals(method.ReturnType.ToDisplayString(), expectedReturnType, StringComparison.Ordinal);

            if (!validSignature)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(ZV0013,
                    attrData?.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                        ?? member.Locations.FirstOrDefault(),
                    method.Name));
            }
        }
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

    private static void ReportZV0011IfApplicable(
        SourceProductionContext ctx,
        IPropertySymbol prop,
        ISymbol member,
        AttributeData validateWithAttr)
    {
        if (prop.Type is not INamedTypeSymbol propNamed) return;

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
