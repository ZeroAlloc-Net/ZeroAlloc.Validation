using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

internal static class RuleEmitter
{
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";
    private const string StopOnFirstFailureFqn = "ZeroAlloc.Validation.StopOnFirstFailureAttribute";
    private const string DisplayNameAttributeFqn = "ZeroAlloc.Validation.DisplayNameAttribute";
    private const string SkipWhenAttributeFqn = "ZeroAlloc.Validation.SkipWhenAttribute";
    private const string CustomValidationAttributeFqn = "ZeroAlloc.Validation.CustomValidationAttribute";

    private const string NotNullFqn               = "ZeroAlloc.Validation.NotNullAttribute";
    private const string NotEmptyFqn              = "ZeroAlloc.Validation.NotEmptyAttribute";
    private const string MinLengthFqn             = "ZeroAlloc.Validation.MinLengthAttribute";
    private const string MaxLengthFqn             = "ZeroAlloc.Validation.MaxLengthAttribute";
    private const string GreaterThanFqn           = "ZeroAlloc.Validation.GreaterThanAttribute";
    private const string LessThanFqn              = "ZeroAlloc.Validation.LessThanAttribute";
    private const string InclusiveBetweenFqn      = "ZeroAlloc.Validation.InclusiveBetweenAttribute";
    private const string GreaterThanOrEqualToFqn  = "ZeroAlloc.Validation.GreaterThanOrEqualToAttribute";
    private const string LessThanOrEqualToFqn     = "ZeroAlloc.Validation.LessThanOrEqualToAttribute";
    private const string ExclusiveBetweenFqn      = "ZeroAlloc.Validation.ExclusiveBetweenAttribute";
    private const string LengthFqn                = "ZeroAlloc.Validation.LengthAttribute";
    private const string EmailAddressFqn          = "ZeroAlloc.Validation.EmailAddressAttribute";
    private const string MatchesFqn               = "ZeroAlloc.Validation.MatchesAttribute";
    private const string NullFqn                  = "ZeroAlloc.Validation.NullAttribute";
    private const string EmptyFqn                 = "ZeroAlloc.Validation.EmptyAttribute";
    private const string EqualFqn                 = "ZeroAlloc.Validation.EqualAttribute";
    private const string NotEqualFqn              = "ZeroAlloc.Validation.NotEqualAttribute";
    private const string IsInEnumFqn              = "ZeroAlloc.Validation.IsInEnumAttribute";
    private const string IsEnumNameFqn            = "ZeroAlloc.Validation.IsEnumNameAttribute";
    private const string PrecisionScaleFqn        = "ZeroAlloc.Validation.PrecisionScaleAttribute";
    private const string MustFqn                  = "ZeroAlloc.Validation.MustAttribute";

    /// <summary>
    /// The message a rule without a more specific one fails with: <c>[Must]</c>, and a custom rule
    /// with neither a usage <c>Message</c> nor a <c>[RuleMessage]</c>. One constant keeps the two
    /// from drifting apart.
    /// </summary>
    private const string InvalidFallbackMessage = "{PropertyName} is invalid.";

    private static bool IsRuleAttribute(AttributeData attr)
    {
        var fqn = attr.AttributeClass?.ToDisplayString();
        return fqn is NotNullFqn or NotEmptyFqn or MinLengthFqn or MaxLengthFqn
            or GreaterThanFqn or LessThanFqn or InclusiveBetweenFqn
            or GreaterThanOrEqualToFqn or LessThanOrEqualToFqn
            or ExclusiveBetweenFqn or LengthFqn
            or EmailAddressFqn or MatchesFqn
            or NullFqn or EmptyFqn
            or EqualFqn or NotEqualFqn
            or IsInEnumFqn
            or IsEnumNameFqn
            or PrecisionScaleFqn
            or MustFqn
            || CustomRules.IsCustomRule(attr);
    }

    /// <summary>
    /// Whether <paramref name="prop"/> stops at its first failing rule. Declared either on the
    /// property itself or, to apply it to every property at once, on the model being validated.
    /// The class-level form is read from the validated type only, so it is not inherited from a
    /// base type — the same rule <c>[Validate(StopOnFirstFailure = true)]</c> already follows.
    /// </summary>
    private static bool HasStopOnFirstFailure(IPropertySymbol prop, INamedTypeSymbol? classSymbol = null) =>
        DeclaresStopOnFirstFailure(prop)
        || (classSymbol is not null && DeclaresStopOnFirstFailure(classSymbol));

    private static bool DeclaresStopOnFirstFailure(ISymbol symbol) =>
        symbol.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), StopOnFirstFailureFqn, StringComparison.Ordinal));

    /// <summary>
    /// Emits the body of the generated <c>Validate</c> method, or, for a model that
    /// <see cref="RequiresAsync(INamedTypeSymbol, Compilation)"/>, the body of its <c>async</c>
    /// validation method, which awaits asynchronous rules and nested validators and reads the
    /// cancellation token <see cref="GeneratedCalls.CancellationToken"/>. <paramref name="calls"/> writes the
    /// lines that hold rule calls; by default it wraps each one the compiler warned on, as
    /// <see cref="MethodCallProbe.CallWarnings"/> reports, in a pragma for that warning, which
    /// ZV0032 mirrors at the attribute. <see cref="MethodCallProbe"/> passes a recording one to
    /// compile this same body.
    /// </summary>
    public static void EmitValidateBody(StringBuilder sb, INamedTypeSymbol classSymbol, Compilation compilation, string modelParamName = "instance", GeneratedFields? fields = null, CallLineWriter? calls = null)
    {
        calls ??= CallLineWriter.Emitting(MethodCallProbe.CallWarnings(compilation, classSymbol));
        bool isAsync = RequiresAsync(classSymbol, compilation);

        // A [SkipWhen] method the validator cannot call is reported, as ZV0017, ZV0028 or ZV0030,
        // and left out, so the model is validated. So is one whose call raises CS0619, which
        // ZV0032 reports.
        if (ResolveSkipWhen(compilation, classSymbol) is { Resolution.IsEmitted: true } skipWhen
            && calls.TryAppendLine(sb, $"        if ({GeneratedCalls.SkipWhenCondition(modelParamName, skipWhen.MethodName)})",
                new[] { SkipWhenSite(classSymbol, in skipWhen, modelParamName) }))
        {
            sb.AppendLine($"            return new global::ZeroAlloc.Validation.ValidationResult(global::System.Array.Empty<global::ZeroAlloc.Validation.ValidationFailure>());");
            sb.AppendLine();
        }

        var byProperty = CollectPropertyRules(classSymbol, compilation);
        var nestedProperties = GetNestedValidateProperties(classSymbol, compilation).ToList();
        var collectionProperties = GetCollectionValidateProperties(classSymbol, compilation).ToList();
        var validatorFields = NestedValidatorFieldsByProperty(classSymbol, compilation);
        var customMethods = CollectCustomValidationMethods(classSymbol, compilation);
        bool hasNested = nestedProperties.Count > 0 || collectionProperties.Count > 0 || customMethods.Count > 0;
        int totalDirectRules = byProperty.Sum(x => x.Rules.Count);

        var validateAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => string.Equals(a.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, StringComparison.Ordinal));
        bool validatorStop = GetBoolNamedArg(validateAttr, "StopOnFirstFailure");

        if (hasNested)
            EmitNestedPath(sb, classSymbol, compilation, byProperty, nestedProperties, collectionProperties, validatorFields, customMethods, modelParamName, validatorStop, totalDirectRules, calls, isAsync, fields);
        else
            EmitFlatPath(sb, classSymbol, byProperty, totalDirectRules, modelParamName, validatorStop, calls, isAsync, fields);
    }

    /// <summary>
    /// Whether the validator for <paramref name="classSymbol"/> must validate asynchronously: a
    /// rule it emits is an <c>AsyncValidationAttribute&lt;T&gt;</c>, or a nested or collection
    /// property is validated by the generated validator of a model that must, transitively. Its
    /// body is then emitted as the body of an <c>async</c> method, awaiting those rules and every
    /// nested validator's <c>ValidateAsync</c>, and its synchronous <c>Validate</c> throws rather
    /// than skip them. The rules counted are the ones <see cref="CollectPropertyRules"/> keeps, so
    /// the async body always awaits something. A <c>[ValidateWith]</c> validator is not generated
    /// here, so it does not count; an async body still calls its <c>ValidateAsync</c>.
    /// </summary>
    public static bool RequiresAsync(INamedTypeSymbol classSymbol, Compilation compilation)
    {
        var cache = AsyncModels.GetValue(compilation, static _ => new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal));
        var key = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (cache.TryGetValue(key, out var requiresAsync)) return requiresAsync;

        requiresAsync = RequiresAsync(classSymbol, compilation, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
        cache.TryAdd(key, requiresAsync);
        return requiresAsync;
    }

    /// <summary>
    /// <see cref="RequiresAsync(INamedTypeSymbol, Compilation)"/>'s answers per compilation, which
    /// every emit of a model's body, its probes and ZV0034 ask for. A <see cref="Compilation"/> is
    /// immutable, so an answer never goes stale, and the table holds it weakly. Only the model
    /// asked about is stored: a model reached inside a cycle is answered by its own first visit.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, System.Collections.Concurrent.ConcurrentDictionary<string, bool>> AsyncModels = new();

    private static bool RequiresAsync(INamedTypeSymbol classSymbol, Compilation compilation, HashSet<INamedTypeSymbol> visited)
    {
        // A model that reaches itself through a cycle is decided by its first visit.
        if (!visited.Add(classSymbol)) return false;

        foreach (var (prop, rules) in CollectPropertyRules(classSymbol, compilation))
        {
            if (!ObsoleteErrors.IsObsoleteError(prop) && rules.Exists(CustomRules.IsAsyncRule))
                return true;
        }

        foreach (var (_, type, isValidateWith, _) in ValidatorDependencies.Of(classSymbol, compilation))
        {
            if (!isValidateWith && RequiresAsync(type, compilation, visited))
                return true;
        }
        return false;
    }

    /// <summary>The return type of <c>ValidateAsync</c>.</summary>
    public const string AsyncResultType = "global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult>";

    /// <summary>The failure buffer a body collects into: <c>FailureBuffer</c>, or its non-ref counterpart for an async body.</summary>
    private static string FailureBufferType(bool isAsync) => isAsync
        ? "global::ZeroAlloc.Validation.Internal.AsyncFailureBuffer"
        : "global::ZeroAlloc.Validation.Internal.FailureBuffer";

    /// <summary>
    /// The rules to emit for each property, in declaration order. Every emit path, sync and async,
    /// builds its rule indices, and so its <c>__Rule_{Prop}_{i}</c> field names, from this one
    /// filtered list, so both paths name the same fields. The rules <see cref="UsableRules"/>
    /// leaves out are left out here too, and so is a rule whose <c>When</c>, <c>Unless</c> or
    /// <c>[Must]</c> method the validator cannot call. Nothing is reported here:
    /// <see cref="ReportRuleUsageDiagnostics"/> reports a usage once, for the type declaring it,
    /// however many validators walk it, issue #290.
    /// </summary>
    private static List<(IPropertySymbol Property, List<AttributeData> Rules)> CollectPropertyRules(
        INamedTypeSymbol classSymbol,
        Compilation compilation)
    {
        var byProperty = new List<(IPropertySymbol Property, List<AttributeData> Rules)>();
        foreach (var member in MemberWalker.GetMembersIncludingBase(classSymbol, compilation))
        {
            if (member is not IPropertySymbol prop) continue;

            var propRules = UsableRules(compilation, prop, ctx: null)
                .FindAll(attr => CallsOnlyReachableMethods(compilation, classSymbol, prop, attr));
            if (propRules.Count > 0)
                byProperty.Add((prop, propRules));
        }
        return byProperty;
    }

    /// <summary>
    /// Reports what is wrong with the rules on <paramref name="prop"/>, for the pipeline step of
    /// the type that declares it. A rule <see cref="UsableRules"/> leaves out is reported as ZV0020,
    /// ZV0021, ZV0023 or ZV0033. The rules that stay are checked for what their emitted code gets
    /// wrong: ZV0016 for a built-in rule on a multi-property value object, ZV0022 for an unknown
    /// placeholder in a custom rule's message. Each depends on the usage alone, not on the model
    /// or on whether the rule's <c>When</c>, <c>Unless</c> and <c>[Must]</c> methods can be called
    /// from it, so every validator that walks the property would find the same ones.
    /// </summary>
    public static void ReportRuleUsageDiagnostics(DiagnosticSink ctx, IPropertySymbol prop, Compilation compilation)
    {
        var usableRules = UsableRules(compilation, prop, ctx);
        ReportZV0016IfApplicable(ctx, prop, usableRules);
        ReportZV0022IfApplicable(ctx, prop, usableRules);
    }

    /// <summary>
    /// The rules on <paramref name="prop"/> a validator can emit, whatever the model. A custom rule
    /// is left out for a property type with no implicit conversion to the rule's <c>T</c>, ZV0021,
    /// or an attribute the validator cannot reach, ZV0023. A numeric comparison rule on a type
    /// <c>Convert.ToDouble</c> cannot convert is left out, ZV0033, and so is a built-in rule that
    /// has no form for a value whose type is a type parameter, ZV0036. A
    /// <c>ValidationAttribute</c> subclass that is neither a built-in nor a custom rule is not a
    /// rule, ZV0020. Each is reported only when <paramref name="ctx"/> is set.
    /// </summary>
    private static List<AttributeData> UsableRules(Compilation compilation, IPropertySymbol prop, DiagnosticSink? ctx)
    {
        var usableRules = new List<AttributeData>();
        foreach (var attr in prop.GetAttributes())
        {
            if (!IsRuleAttribute(attr))
            {
                ReportZV0020IfApplicable(ctx, prop, attr);
                continue;
            }

            if (CustomRules.IsCustomRule(attr) && !CanEmitCustomRule(compilation, prop, attr, ctx))
                continue;

            if (!CanCompareAsNumber(compilation, prop, attr, ctx))
                continue;

            if (!HasTypeParameterForm(prop, attr, ctx))
                continue;

            usableRules.Add(attr);
        }
        return usableRules;
    }

    private static void EmitNestedPath(
        StringBuilder sb,
        INamedTypeSymbol classSymbol,
        Compilation compilation,
        List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
        List<IPropertySymbol> nestedProperties,
        List<(IPropertySymbol Property, INamedTypeSymbol ElementType)> collectionProperties,
        Dictionary<IPropertySymbol, string> validatorFields,
        List<CustomValidationCall> customMethods,
        string modelParamName,
        bool validatorStop,
        int totalDirectRules,
        CallLineWriter calls,
        bool isAsync,
        GeneratedFields? fields = null)
    {
        sb.AppendLine($"        var _buf = new {FailureBufferType(isAsync)}({totalDirectRules});");
        sb.AppendLine();

        if (!validatorStop)
        {
            EmitPropertyRulesWithAdd(sb, byProperty, classSymbol, modelParamName, calls, fields);
            EmitNestedValidators(sb, nestedProperties, validatorFields, modelParamName, isAsync);
            EmitCollectionValidators(sb, collectionProperties, validatorFields, modelParamName, isAsync);
        }
        else
        {
            EmitNestedPathStop(sb, classSymbol, compilation, byProperty, nestedProperties, collectionProperties, validatorFields, modelParamName, calls, isAsync, fields);
        }

        // [CustomValidation] methods always run last.
        // With validatorStop=true: EmitNestedPathStop above emits early returns for each failing property group,
        // so custom methods are only reached if all property groups pass.
        EmitCustomValidationCalls(sb, customMethods, modelParamName, calls, isAsync);

        sb.AppendLine("        return _buf.ToResult();");
    }

    private static void EmitCustomValidationCalls(StringBuilder sb, List<CustomValidationCall> customMethods, string modelParamName, CallLineWriter calls, bool isAsync)
    {
        for (int i = 0; i < customMethods.Count; i++)
        {
            var call = GeneratedCalls.CustomValidationCall(modelParamName, customMethods[i].Method.Name, customMethods[i].Receiver);
            var site = new[] { CustomValidationSite(customMethods[i], call) };
            // A call that raises CS0619, which ZV0032 reports, is left out with the loop over
            // its result.
            if (customMethods[i].ByRef && isAsync)
            {
                // An async body cannot declare a span local, so the span goes straight to the
                // buffer, which walks it by reference.
                if (calls.TryAppendLine(sb, $"        _buf.AddRange({call});", site))
                    sb.AppendLine();
                continue;
            }
            if (customMethods[i].ByRef)
            {
                // A span is walked by reference to avoid copying each failure. The call is hoisted
                // into a local because `ref readonly` iteration needs an addressable variable
                // rather than a call expression. Arrays cannot be iterated this way — their foreach
                // lowers to indexing — but they allocate nothing either way.
                if (!calls.TryAppendLine(sb, $"        var _cv{i} = {call};", site)) continue;
                sb.AppendLine($"        foreach (ref readonly var _cf in _cv{i})");
            }
            else if (!calls.TryAppendLine(sb, $"        foreach (var _cf in {call})", site))
            {
                continue;
            }
            sb.AppendLine("            _buf.Add(_cf);");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Return types a <c>[CustomValidation]</c> method may declare. <c>IEnumerable&lt;T&gt;</c> is
    /// the original and still supported, but a method written with <c>yield</c> allocates its
    /// iterator state machine on every call — including when it yields nothing — so an array or a
    /// span is offered as the allocation-free alternative.
    /// </summary>
    private static bool IsCustomValidationReturnType(ITypeSymbol returnType, out bool byRef)
    {
        byRef = false;
        var display = returnType.ToDisplayString();

        if (string.Equals(display, "System.Collections.Generic.IEnumerable<ZeroAlloc.Validation.ValidationFailure>", StringComparison.Ordinal))
            return true;

        // An array's foreach already compiles to indexing and allocates nothing, and it cannot be
        // iterated `ref readonly`, so it takes the plain form.
        if (string.Equals(display, "ZeroAlloc.Validation.ValidationFailure[]", StringComparison.Ordinal))
            return true;

        if (string.Equals(display, "System.ReadOnlySpan<ZeroAlloc.Validation.ValidationFailure>", StringComparison.Ordinal))
        {
            byRef = true;
            return true;
        }

        return false;
    }

    private static List<CustomValidationCall> CollectCustomValidationMethods(INamedTypeSymbol classSymbol, Compilation compilation)
    {
        var result = new List<CustomValidationCall>();
        foreach (var (method, byRef) in CustomValidationMethods(classSymbol, compilation))
            result.Add(new CustomValidationCall(method, byRef, CustomValidationReceiver(compilation, classSymbol, method)));
        return result;
    }

    /// <summary>
    /// The <c>[CustomValidation]</c> methods the validator calls: a signature ZV0013 accepts, and
    /// not static or inaccessible, which is ZV0028.
    /// </summary>
    private static IEnumerable<(IMethodSymbol Method, bool ByRef)> CustomValidationMethods(INamedTypeSymbol classSymbol, Compilation compilation)
    {
        foreach (var member in MemberWalker.GetMembersIncludingBase(classSymbol, compilation))
        {
            if (member is not IMethodSymbol method) continue;
            if (FindCustomValidationAttribute(method, out _) is null) continue;
            // Only emit if the signature is one ZV0013 accepts: not generic, no parameters, and a
            // return type the generated validator knows how to walk.
            if (method.IsGenericMethod || method.Parameters.Length != 0) continue;
            if (!IsCustomValidationReturnType(method.ReturnType, out var byRef)) continue;
            // A static method, or one the validator cannot access, is reported as ZV0028.
            if (MethodReachability.Classify(compilation, classSymbol, method) != MethodReach.Callable) continue;
            yield return (method, byRef);
        }
    }

    /// <summary>
    /// The <c>[CustomValidation]</c> attribute <paramref name="method"/> carries, declared on it
    /// or on a method it overrides, or <see langword="null"/> when it has none.
    /// <paramref name="declaration"/> is the method the attribute is written on. The attribute
    /// keeps the default <c>Inherited = true</c>, so an override that does not repeat it still
    /// carries it, issue #240. <see cref="MemberWalker.GetMembersIncludingBase"/> yields only the
    /// most-derived override, so the validator makes one call, which dispatches to it.
    /// </summary>
    public static AttributeData? FindCustomValidationAttribute(IMethodSymbol method, out IMethodSymbol declaration)
    {
        for (IMethodSymbol? current = method; current is not null; current = current.OverriddenMethod)
        {
            foreach (var attr in current.GetAttributes())
            {
                if (string.Equals(attr.AttributeClass?.ToDisplayString(), CustomValidationAttributeFqn, StringComparison.Ordinal))
                {
                    declaration = current;
                    return attr;
                }
            }
        }
        declaration = method;
        return null;
    }

    private static string CustomValidationStatement(IMethodSymbol method) =>
        MethodCallProbe.ValueStatement(GeneratedCalls.CustomValidationCall(MethodCallProbe.Model, method.Name, receiverType: null));

    /// <summary>
    /// The type to call <paramref name="method"/> through, or <see langword="null"/> to call it on
    /// the model itself. The compiler, through <see cref="MethodCallProbe"/>, says what
    /// <c>instance.Check()</c> binds to. A more-derived type declaring another <c>Check</c> the
    /// call applies to, such as a <c>Check(int x = 0)</c> or a static <c>Check()</c>, would take
    /// the call. It is then made through the type that declares the method, or the virtual method
    /// it overrides, where <c>Check()</c> binds to it: any other overload applicable without
    /// arguments there needs a default value or is static, and loses to it. The call is still
    /// virtual. A call that binds to the method itself but still fails, as CS0619 does for an
    /// <c>[Obsolete(error: true)]</c> method, is made on the model too: a cast would not change
    /// what it binds to, and ZV0032 names the call as the user wrote it.
    /// </summary>
    private static string? CustomValidationReceiver(Compilation compilation, INamedTypeSymbol classSymbol, IMethodSymbol method)
    {
        var root = method;
        while (root.OverriddenMethod is { } overridden) root = overridden;

        if (CertainCall.CustomValidation(compilation, classSymbol, method)) return null;

        var resolution = MethodCallProbe.Resolve(compilation, classSymbol, method.Name, CustomValidationStatement(method));
        if (resolution is { Reach: MethodReach.Callable or MethodReach.NotFound, Method: { } bound }
            && SymbolEqualityComparer.Default.Equals(bound.OriginalDefinition, root.OriginalDefinition))
            return null;

        return root.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static void EmitNestedPathStop(
        StringBuilder sb,
        INamedTypeSymbol classSymbol,
        Compilation compilation,
        List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
        List<IPropertySymbol> nestedProperties,
        List<(IPropertySymbol Property, INamedTypeSymbol ElementType)> collectionProperties,
        Dictionary<IPropertySymbol, string> validatorFields,
        string modelParamName,
        CallLineWriter calls,
        bool isAsync,
        GeneratedFields? fields = null)
    {
        int groupIdx = 0;
        int collCi = 0;

        foreach (var member in MemberWalker.GetMembersIncludingBase(classSymbol, compilation))
        {
            if (member is not IPropertySymbol prop) continue;

            FindPropertyGroups(prop, byProperty, nestedProperties, collectionProperties,
                out var directProp, out var directRules, out var nestedProp, out var collProp, out var collElementType);

            if (directProp is null && nestedProp is null && collProp is null) continue;

            // All validation for this property (direct rules + nested + collection) is treated as
            // one group. A single snapshot covers all three so that a null-check failure on a
            // nested property also suppresses the nested validator call result — consistent with
            // FluentValidation's per-property cascade semantics.
            sb.AppendLine($"        int _b{groupIdx} = _buf.Count;");

            if (directProp is not null && directRules is not null)
                EmitPropertyRulesForProp(sb, directProp, directRules, classSymbol, modelParamName, calls, fields);

            if (nestedProp is not null)
                EmitNestedValidatorForProp(sb, nestedProp, ValidatorField(validatorFields, nestedProp), modelParamName, isAsync);

            if (collProp is not null && collElementType is not null)
                EmitCollectionValidatorForProp(sb, collProp, collElementType, ValidatorField(validatorFields, collProp), collCi++, modelParamName, isAsync);

            sb.AppendLine($"        if (_buf.Count > _b{groupIdx}) return _buf.ToResult();");
            sb.AppendLine();
            groupIdx++;
        }
    }

    private static void FindPropertyGroups(
        IPropertySymbol prop,
        List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
        List<IPropertySymbol> nestedProperties,
        List<(IPropertySymbol Property, INamedTypeSymbol ElementType)> collectionProperties,
        out IPropertySymbol? directProp,
        out List<AttributeData>? directRules,
        out IPropertySymbol? nestedProp,
        out IPropertySymbol? collProp,
        out INamedTypeSymbol? collElementType)
    {
        directProp = null;
        directRules = null;
        for (int bi = 0; bi < byProperty.Count; bi++)
        {
            if (SymbolEqualityComparer.Default.Equals(byProperty[bi].Property, prop))
            {
                directProp = byProperty[bi].Property;
                directRules = byProperty[bi].Rules;
                break;
            }
        }

        nestedProp = null;
        for (int ni = 0; ni < nestedProperties.Count; ni++)
        {
            if (SymbolEqualityComparer.Default.Equals(nestedProperties[ni], prop))
            {
                nestedProp = nestedProperties[ni];
                break;
            }
        }

        collProp = null;
        collElementType = null;
        for (int ci = 0; ci < collectionProperties.Count; ci++)
        {
            if (SymbolEqualityComparer.Default.Equals(collectionProperties[ci].Property, prop))
            {
                collProp = collectionProperties[ci].Property;
                collElementType = collectionProperties[ci].ElementType;
                break;
            }
        }
    }

    private static void EmitPropertyRulesWithAdd(
        StringBuilder sb,
        List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
        INamedTypeSymbol? classSymbol,
        string modelParamName,
        CallLineWriter calls,
        GeneratedFields? fields = null)
    {
        for (int pi = 0; pi < byProperty.Count; pi++)
        {
            var (prop, rules) = byProperty[pi];
            EmitPropertyRulesForProp(sb, prop, rules, classSymbol, modelParamName, calls, fields);
        }
    }

    private static void EmitPropertyRulesForProp(
        StringBuilder sb,
        IPropertySymbol prop,
        List<AttributeData> rules,
        INamedTypeSymbol? classSymbol,
        string modelParamName,
        CallLineWriter calls,
        GeneratedFields? fields = null)
    {
        var propName = prop.Name;
        var displayName = GetDisplayName(prop) ?? propName;
        var propAccess = BuildPropertyAccess(modelParamName, prop);
        var rawPropAccess = GeneratedCalls.RawPropertyAccess(modelParamName, prop);
        var stopMode = HasStopOnFirstFailure(prop, classSymbol);
        var obsoleteError = ObsoleteErrors.IsObsoleteError(prop);

        // A rule on an [Obsolete(error: true)] property is never emitted at all — not even as
        // "if (false)", which the compiler would flag as CS0162 unreachable code in the
        // generated file. ObsoleteErrorRules reports it as ZV0032 instead. emitted counts only
        // the rules this loop does emit, so a later rule's "else if" chains onto the last one
        // actually written, not onto the position it held in the attribute list.
        int emitted = 0;
        for (int i = 0; i < rules.Count; i++)
        {
            if (obsoleteError) continue;

            var attr = rules[i];
            var fqn = attr.AttributeClass!.ToDisplayString();
            var prefix = (stopMode && emitted > 0) ? "        else if" : "        if";
            var ruleMessage = FindCustomRuleMessage(attr);
            var message = ResolveRuleMessage(attr, fqn, displayName, ruleMessage);
            var propTypeFullName = GetNullableUnwrappedFullTypeName(prop);
            var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName, modelParamName, prop.Type, rawPropAccess, propName: prop.Name, ruleIndex: i, fields: fields);
            var propertyValueExpr = message.HasPropertyValue ? BuildPropertyValueExpr(prop, modelParamName) : null;
            var whenMethod   = GetWhen(attr);
            var unlessMethod = GetUnless(attr);
            var whenGuard    = whenMethod   is null ? "" : GeneratedCalls.WhenGuard(modelParamName, whenMethod);
            var unlessGuard  = unlessMethod is null ? "" : GeneratedCalls.UnlessGuard(modelParamName, unlessMethod);

            // A condition whose call raises CS0619, which ZV0032 reports, is left out with its body.
            if (!calls.TryAppendLine(sb, $"{prefix} ({GeneratedCalls.GuardedCondition(whenGuard + unlessGuard, condition)})",
                RuleCallSites(attr, prop, modelParamName, rawPropAccess, ruleIndex: i, condition)))
                continue;
            sb.AppendLine($"            _buf.Add({BuildFailureInitializer(propName, message, attr, ruleMessage, propertyValueExpr)});");
            emitted++;
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Wraps a statement that reads a field of a <c>ref readonly ValidationFailure</c>. EPS06
    /// (ErrorProne.NET.Structs) does not recognise that <c>init</c>-only auto-properties on a
    /// <c>readonly struct</c> cannot mutate it, so it reports a defensive copy that the compiler
    /// never actually emits for a readonly struct. Filed upstream at
    /// https://github.com/SergeyTeplyakov/ErrorProne.NET/issues; tracked internally as #213.
    /// Scoped to the one statement that trips it, so the pragma does not hide a real EPS06
    /// finding anywhere else in the generated file.
    /// </summary>
    private static void AppendFailureCopyPragma(StringBuilder sb, bool disable) =>
        sb.AppendLine(disable
            ? "#pragma warning disable EPS06 // false positive: ValidationFailure is a readonly struct, see #213"
            : "#pragma warning restore EPS06");

    private static void EmitNestedValidators(
        StringBuilder sb,
        List<IPropertySymbol> nestedProperties,
        Dictionary<IPropertySymbol, string> validatorFields,
        string modelParamName,
        bool isAsync)
    {
        for (int ni = 0; ni < nestedProperties.Count; ni++)
            EmitNestedValidatorForProp(sb, nestedProperties[ni], ValidatorField(validatorFields, nestedProperties[ni]), modelParamName, isAsync);
    }

    private static void EmitNestedValidatorForProp(StringBuilder sb, IPropertySymbol nestedProp, string validatorField, string modelParamName, bool isAsync)
    {
        var propName = nestedProp.Name;

        var access = GeneratedCalls.MemberAccess(modelParamName, propName);

        var needsPropGuard = NeedsNullGuard(nestedProp.Type);
        if (needsPropGuard)
        {
            sb.AppendLine($"        if ({access} is not null)");
            sb.AppendLine("        {");
        }
        if (isAsync)
        {
            // An async body cannot walk the failures by reference, so the buffer copies them.
            var validation = GeneratedCalls.Await($"{validatorField}.ValidateAsync({access}, {GeneratedCalls.CancellationToken})");
            sb.AppendLine($"            _buf.AddNested({validation}, \"{propName}\");");
        }
        else
        {
            sb.AppendLine($"            var nestedResult = {validatorField}.Validate({access});");
            sb.AppendLine("            foreach (ref readonly var f in nestedResult.Failures)");
            AppendFailureCopyPragma(sb, disable: true);
            sb.AppendLine($"                _buf.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}.\" + f.PropertyName, ErrorMessage = f.ErrorMessage, ErrorCode = f.ErrorCode, Severity = f.Severity }});");
            AppendFailureCopyPragma(sb, disable: false);
        }
        if (needsPropGuard)
        {
            sb.AppendLine("        }");
        }
        sb.AppendLine();
    }

    private static void EmitCollectionValidators(
        StringBuilder sb,
        List<(IPropertySymbol Property, INamedTypeSymbol ElementType)> collectionProperties,
        Dictionary<IPropertySymbol, string> validatorFields,
        string modelParamName,
        bool isAsync)
    {
        for (int ci = 0; ci < collectionProperties.Count; ci++)
        {
            var property = collectionProperties[ci].Property;
            EmitCollectionValidatorForProp(sb, property, collectionProperties[ci].ElementType, ValidatorField(validatorFields, property), ci, modelParamName, isAsync);
        }
    }

    private static void EmitCollectionValidatorForProp(StringBuilder sb, IPropertySymbol collProp, INamedTypeSymbol elementType, string validatorField, int ci, string modelParamName, bool isAsync)
    {
        var propName = collProp.Name;
        var varName = $"_c{ci.ToString(CultureInfo.InvariantCulture)}";

        var style = ClassifyCollectionIteration(collProp.Type);
        // An async body cannot hold a span or a ref local across an await, so a List<T> is
        // walked by index there, as the interface-typed collections are.
        if (isAsync && style == CollectionIteration.ListSpan)
            style = CollectionIteration.Indexed;

        var access = GeneratedCalls.MemberAccess(modelParamName, propName);

        sb.AppendLine($"        if ({access} is not null)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var {varName}Src = {access};");

        switch (style)
        {
            case CollectionIteration.ListSpan:
                // A span over the backing array: no enumerator, and no interface dispatch per item.
                // Span<T>.Enumerator.Current returns by ref, so the loop variable is bound `ref
                // readonly` — HLQ004 (NetFabric.Hyperlinq.Analyzer) requires this to avoid a copy
                // of each item on every iteration.
                sb.AppendLine($"            int {varName}Idx = 0;");
                sb.AppendLine($"            foreach (ref readonly var {varName}Item in global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan({varName}Src))");
                sb.AppendLine("            {");
                break;

            case CollectionIteration.Indexed:
                // foreach over an interface-typed collection boxes its enumerator on every call,
                // valid path included. Indexing avoids creating one at all.
                sb.AppendLine($"            for (int {varName}Idx = 0; {varName}Idx < {varName}Src.Count; {varName}Idx++)");
                sb.AppendLine("            {");
                sb.AppendLine($"                var {varName}Item = {varName}Src[{varName}Idx];");
                break;

            default:
                sb.AppendLine($"            int {varName}Idx = 0;");
                sb.AppendLine($"            foreach (var {varName}Item in {varName}Src)");
                sb.AppendLine("            {");
                break;
        }

        EmitCollectionItemValidation(sb, propName, elementType, validatorField, varName, isAsync);
        if (style != CollectionIteration.Indexed)
            sb.AppendLine($"                {varName}Idx++;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    /// <summary>
    /// The validation of one collection element, inside the loop <see cref="EmitCollectionValidatorForProp"/>
    /// opens: the element's validator, its failures added under <c>Prop[i].</c>.
    /// </summary>
    private static void EmitCollectionItemValidation(StringBuilder sb, string propName, INamedTypeSymbol elementType, string validatorField, string varName, bool isAsync)
    {
        var needsItemGuard = NeedsNullGuard(elementType);
        if (needsItemGuard)
        {
            sb.AppendLine($"                if ({varName}Item is not null)");
            sb.AppendLine("                {");
        }
        if (isAsync)
        {
            var validation = GeneratedCalls.Await($"{validatorField}.ValidateAsync({varName}Item, {GeneratedCalls.CancellationToken})");
            sb.AppendLine($"                    _buf.AddNested({validation}, \"{propName}\", {varName}Idx);");
        }
        else
        {
            sb.AppendLine($"                    var {varName}Result = {validatorField}.Validate({varName}Item);");
            sb.AppendLine($"                    foreach (ref readonly var f in {varName}Result.Failures)");
            AppendFailureCopyPragma(sb, disable: true);
            sb.AppendLine($"                        _buf.Add(new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}[\" + {varName}Idx + \"].\" + f.PropertyName, ErrorMessage = f.ErrorMessage, ErrorCode = f.ErrorCode, Severity = f.Severity }});");
            AppendFailureCopyPragma(sb, disable: false);
        }
        if (needsItemGuard)
        {
            sb.AppendLine("                }");
        }
    }

    private enum CollectionIteration
    {
        /// <summary>Arrays and anything else whose foreach is already allocation-free.</summary>
        Foreach,
        /// <summary><c>List&lt;T&gt;</c>, iterated as a span over its backing array.</summary>
        ListSpan,
        /// <summary>An interface exposing <c>Count</c> and an indexer, iterated by index.</summary>
        Indexed,
    }

    /// <summary>
    /// How to walk a collection property without allocating. <c>foreach</c> over an array uses the
    /// indexer and over <c>List&lt;T&gt;</c> a struct enumerator, but over an interface it boxes the
    /// enumerator on every validation — including when the model is valid — so interface-typed
    /// collections are walked by index instead.
    /// </summary>
    private static CollectionIteration ClassifyCollectionIteration(ITypeSymbol propertyType)
    {
        if (propertyType is IArrayTypeSymbol)
            return CollectionIteration.Foreach;

        if (propertyType is not INamedTypeSymbol named || !named.IsGenericType)
            return CollectionIteration.Foreach;

        return named.OriginalDefinition.ToDisplayString() switch
        {
            "System.Collections.Generic.List<T>" => CollectionIteration.ListSpan,
            "System.Collections.Generic.IList<T>" => CollectionIteration.Indexed,
            "System.Collections.Generic.IReadOnlyList<T>" => CollectionIteration.Indexed,
            _ => CollectionIteration.Foreach,
        };
    }

    private static void EmitFlatPath(
        StringBuilder sb,
        INamedTypeSymbol classSymbol,
        List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
        int totalDirectRules,
        string modelParamName,
        bool validatorStop,
        CallLineWriter calls,
        bool isAsync,
        GeneratedFields? fields = null)
    {
        // Under model-level fail-fast, a group that can only ever produce one failure returns
        // that failure's array directly — no scratch buffer, no copy. When every group is like
        // that the buffer is never touched at all, so it is not even declared.
        var direct = new bool[byProperty.Count];
        bool needsBuffer = false;
        for (int pi = 0; pi < byProperty.Count; pi++)
        {
            direct[pi] = validatorStop && YieldsAtMostOneFailure(byProperty[pi].Property, byProperty[pi].Rules, classSymbol);
            if (!direct[pi]) needsBuffer = true;
        }

        if (needsBuffer)
        {
            // FailureBuffer rents from ArrayPool and only on the first Add, so the valid path
            // neither allocates nor touches the pool, and a failing path costs the result array
            // alone rather than a scratch array plus the result.
            sb.AppendLine($"        var _buf = new {FailureBufferType(isAsync)}({totalDirectRules});");
            sb.AppendLine();
        }

        for (int pi = 0; pi < byProperty.Count; pi++)
        {
            if (validatorStop && !direct[pi])
                sb.AppendLine($"        int _b{pi} = _buf.Count;");

            EmitFlatPathPropertyRules(sb, byProperty[pi].Property, byProperty[pi].Rules, totalDirectRules, modelParamName, calls, fields, direct[pi], classSymbol);

            if (validatorStop && !direct[pi])
                EmitFlatPathStopOnFirstFailureReturn(sb, pi);

            sb.AppendLine();
        }

        if (!needsBuffer)
        {
            sb.AppendLine("        return new global::ZeroAlloc.Validation.ValidationResult(global::System.Array.Empty<global::ZeroAlloc.Validation.ValidationFailure>());");
            return;
        }

        sb.AppendLine("        return _buf.ToResult();");
    }

    /// <summary>
    /// Whether a property group can contribute at most one failure: either it carries a single
    /// rule, or property-level <c>[StopOnFirstFailure]</c> chains its rules so only the first
    /// matching one fires. Under model-level fail-fast such a group is the last thing the
    /// validator does, so its failure can be returned directly.
    /// </summary>
    private static bool YieldsAtMostOneFailure(IPropertySymbol prop, List<AttributeData> rules, INamedTypeSymbol? classSymbol) =>
        rules.Count == 1 || HasStopOnFirstFailure(prop, classSymbol);

    private static void EmitFlatPathPropertyRules(
        StringBuilder sb,
        IPropertySymbol prop,
        List<AttributeData> rules,
        int totalDirectRules,
        string modelParamName,
        CallLineWriter calls,
        GeneratedFields? fields = null,
        bool directReturn = false,
        INamedTypeSymbol? classSymbol = null)
    {
        var propName = prop.Name;
        var displayName = GetDisplayName(prop) ?? propName;
        var propAccess = BuildPropertyAccess(modelParamName, prop);
        var rawPropAccess = GeneratedCalls.RawPropertyAccess(modelParamName, prop);
        var stopMode = HasStopOnFirstFailure(prop, classSymbol);
        var obsoleteError = ObsoleteErrors.IsObsoleteError(prop);

        // See EmitPropertyRulesForProp: a rule on an [Obsolete(error: true)] property is left
        // out entirely, never emitted as "if (false)", and emitted tracks the "else if" chain
        // across whatever rules that leaves.
        int emitted = 0;
        for (int i = 0; i < rules.Count; i++)
        {
            if (obsoleteError) continue;

            var attr = rules[i];
            var fqn = attr.AttributeClass!.ToDisplayString();
            var prefix = (stopMode && emitted > 0) ? "        else if" : "        if";
            var ruleMessage = FindCustomRuleMessage(attr);
            var message = ResolveRuleMessage(attr, fqn, displayName, ruleMessage);
            var propTypeFullName = GetNullableUnwrappedFullTypeName(prop);
            var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName, modelParamName, prop.Type, rawPropAccess, propName: prop.Name, ruleIndex: i, fields: fields);
            var propertyValueExpr = message.HasPropertyValue ? BuildPropertyValueExpr(prop, modelParamName) : null;
            var whenMethod   = GetWhen(attr);
            var unlessMethod = GetUnless(attr);
            var whenGuard    = whenMethod   is null ? "" : GeneratedCalls.WhenGuard(modelParamName, whenMethod);
            var unlessGuard  = unlessMethod is null ? "" : GeneratedCalls.UnlessGuard(modelParamName, unlessMethod);

            // A condition whose call raises CS0619, which ZV0032 reports, is left out with its body.
            if (!calls.TryAppendLine(sb, $"{prefix} ({GeneratedCalls.GuardedCondition(whenGuard + unlessGuard, condition)})",
                RuleCallSites(attr, prop, modelParamName, rawPropAccess, ruleIndex: i, condition)))
                continue;
            sb.AppendLine("        {");
            if (directReturn)
            {
                sb.AppendLine("            return new global::ZeroAlloc.Validation.ValidationResult(new global::ZeroAlloc.Validation.ValidationFailure[]");
                sb.AppendLine("            {");
                sb.AppendLine($"                {BuildFailureInitializer(propName, message, attr, ruleMessage, propertyValueExpr)}");
                sb.AppendLine("            });");
            }
            else
            {
                sb.AppendLine($"            _buf.Add({BuildFailureInitializer(propName, message, attr, ruleMessage, propertyValueExpr)});");
            }
            sb.AppendLine("        }");
            emitted++;
        }
    }

    private static void EmitFlatPathStopOnFirstFailureReturn(StringBuilder sb, int pi)
    {
        sb.AppendLine($"        if (_buf.Count > _b{pi}) return _buf.ToResult();");
    }

    /// <summary>
    /// The compile-time message for one rule usage. Built-in rules keep their existing handling.
    /// A custom rule takes the usage's <c>Message</c>, then its nearest <c>[RuleMessage]</c>, then
    /// the <c>[Must]</c> fallback, with named placeholders bound to the arguments written on the
    /// usage. An unknown placeholder is emitted literally; <see cref="ReportZV0022IfApplicable"/>
    /// reports it. <paramref name="ruleMessage"/> is the usage's
    /// <see cref="FindCustomRuleMessage"/>, resolved once by the caller.
    /// </summary>
    private static MessageTemplate ResolveRuleMessage(
        AttributeData attr,
        string fqn,
        string displayName,
        (string Message, string? ErrorCode)? ruleMessage)
    {
        if (!CustomRules.IsCustomRule(attr))
            return ResolveMessage(attr, fqn, displayName)
                ?? MessageTemplate.Literal(GetDefaultMessage(fqn, attr, displayName));

        return CustomRules.ResolveMessage(CustomRuleTemplate(attr, ruleMessage), attr, displayName, out _);
    }

    /// <summary>
    /// A custom rule usage's message template: its own <c>Message</c>, then
    /// <paramref name="ruleMessage"/>, its nearest <c>[RuleMessage]</c>, then the <c>[Must]</c>
    /// fallback.
    /// </summary>
    private static string CustomRuleTemplate(AttributeData attr, (string Message, string? ErrorCode)? ruleMessage) =>
        GetMessage(attr)
            ?? ruleMessage?.Message
            ?? InvalidFallbackMessage;

    /// <summary>
    /// Fires ZV0022 for each placeholder in a custom rule's message that matches no argument
    /// written on the usage and so is emitted literally. Which placeholders are unknown depends on
    /// the usage alone; <c>{PropertyName}</c> always resolves, whatever the display name. A
    /// property marked <c>[Obsolete(error: true)]</c> gets no rule emitted at all, ZV0032, so its
    /// messages are not checked.
    /// </summary>
    private static void ReportZV0022IfApplicable(DiagnosticSink ctx, IPropertySymbol prop, List<AttributeData> rules)
    {
        if (ObsoleteErrors.IsObsoleteError(prop)) return;

        for (int i = 0; i < rules.Count; i++)
        {
            var attr = rules[i];
            if (!CustomRules.IsCustomRule(attr)) continue;

            CustomRules.ResolveMessage(CustomRuleTemplate(attr, FindCustomRuleMessage(attr)), attr, prop.Name, out var unknown);
            foreach (var name in unknown)
            {
                ctx.Report(
                    ZV0022,
                    AttributeLocation(attr, prop),
                    name, attr.AttributeClass!.Name, prop.Name);
            }
        }
    }

    /// <summary>
    /// The <c>[RuleMessage]</c> a custom rule usage falls back to, or <see langword="null"/> for a
    /// built-in rule or a custom rule without one. Resolved once per usage and passed to both the
    /// message and the error-code lookups.
    /// </summary>
    private static (string Message, string? ErrorCode)? FindCustomRuleMessage(AttributeData attr) =>
        CustomRules.IsCustomRule(attr) ? CustomRules.FindRuleMessage(attr.AttributeClass!) : null;

    /// <summary>
    /// A built-in rule's usage <c>Message</c>, resolved in one pass, or <see langword="null"/> when
    /// the usage sets none. <c>{PropertyName}</c> becomes <paramref name="propName"/>, and the
    /// rule's own placeholders become its arguments: <c>{ComparisonValue}</c> for the comparison
    /// rules, <c>{MinLength}</c> and <c>{MaxLength}</c> for the length rules, and <c>{From}</c> and
    /// <c>{To}</c> for the range rules. Any other name is kept as written.
    /// </summary>
    private static MessageTemplate? ResolveMessage(AttributeData attr, string fqn, string propName)
    {
        var raw = GetMessage(attr);
        if (raw is null) return null;

        return MessageTemplate.Resolve(raw, name => ResolveBuiltInPlaceholder(attr, fqn, propName, name));
    }

    private static string? ResolveBuiltInPlaceholder(AttributeData attr, string fqn, string propName, string name)
    {
        switch (name)
        {
            case "PropertyName":
                return propName;

            case "ComparisonValue" when fqn is GreaterThanFqn or LessThanFqn or GreaterThanOrEqualToFqn
                    or LessThanOrEqualToFqn or EqualFqn or NotEqualFqn:
                return fqn is (EqualFqn or NotEqualFqn) && IsStringArg(attr, 0)
                    ? GetStringArg(attr, 0)
                    : GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture);

            case "MinLength" when fqn is LengthFqn or MinLengthFqn:
                return GetIntArg(attr, 0).ToString(CultureInfo.InvariantCulture);

            case "MaxLength" when fqn is LengthFqn:
                return GetIntArg(attr, 1).ToString(CultureInfo.InvariantCulture);

            case "MaxLength" when fqn is MaxLengthFqn:
                return GetIntArg(attr, 0).ToString(CultureInfo.InvariantCulture);

            case "From" when fqn is ExclusiveBetweenFqn or InclusiveBetweenFqn:
                return GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture);

            case "To" when fqn is ExclusiveBetweenFqn or InclusiveBetweenFqn:
                return GetDoubleArg(attr, 1).ToString(CultureInfo.InvariantCulture);

            default:
                return null;
        }
    }

    private static string? GetMessage(AttributeData attr) =>
        TryGetBaseMemberArgument(attr, "Message", out var value) ? value.Value as string : null;

    /// <summary>
    /// The value written on the usage for the base member <paramref name="name"/>, one of the
    /// members declared on <c>ZeroAlloc.Validation.ValidationAttribute</c>. A named argument that
    /// binds a member of the same name the attribute declares itself, such as a
    /// <c>new Message</c>, is not a base member and is not returned.
    /// </summary>
    private static bool TryGetBaseMemberArgument(AttributeData attr, string name, out TypedConstant value)
    {
        foreach (var named in attr.NamedArguments)
        {
            if (string.Equals(named.Key, name, StringComparison.Ordinal)
                && attr.AttributeClass is { } attrClass
                && CustomRules.IsBaseMemberArgument(attrClass, name))
            {
                value = named.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// The model methods <paramref name="prop"/>'s rules call that the generated validator for
    /// <paramref name="classSymbol"/> cannot: a <c>When</c> or <c>Unless</c> condition, or a
    /// <c>[Must]</c> predicate, that is static or inaccessible, or whose call does not compile
    /// for another reason. Each one means a dropped rule, reported as ZV0028,
    /// as ZV0017 when the method is inaccessible on a base type, or as ZV0030.
    /// </summary>
    public static IEnumerable<ResolvedMethodCall> GetUnreachableMethodCalls(
        Compilation compilation, INamedTypeSymbol classSymbol, IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (!IsRuleAttribute(attr)) continue;

            foreach (var (usage, name, statement, argumentType) in MethodCallsOf(attr, prop))
            {
                var resolution = MethodCallProbe.ResolveCondition(compilation, classSymbol, name, statement, argumentType);
                if (resolution.IsEmitted) continue;
                yield return new ResolvedMethodCall(attr, usage, name, resolution);
            }
        }
    }

    /// <summary>
    /// Whether every model method <paramref name="attr"/> calls can be called from the generated
    /// validator. A rule that calls a static or inaccessible method, or a name that resolves to
    /// no usable method, is dropped here and reported through
    /// <see cref="GetUnreachableMethodCalls"/>, rather than emitted as a call that would not compile.
    /// </summary>
    private static bool CallsOnlyReachableMethods(
        Compilation compilation, INamedTypeSymbol classSymbol, IPropertySymbol prop, AttributeData attr)
    {
        foreach (var (_, name, statement, argumentType) in MethodCallsOf(attr, prop))
        {
            if (!MethodCallProbe.ResolveCondition(compilation, classSymbol, name, statement, argumentType).IsEmitted)
                return false;
        }
        return true;
    }

    /// <summary>
    /// The model methods one rule calls, each with how the diagnostic names its usage and the
    /// statement <see cref="MethodCallProbe"/> compiles for it: the guard or condition exactly as
    /// the emitter writes it, used the way the validator uses it.
    /// </summary>
    private static IEnumerable<(string Usage, string Name, string Statement, ITypeSymbol? ArgumentType)> MethodCallsOf(
        AttributeData attr, IPropertySymbol prop)
    {
        const string model = MethodCallProbe.Model;
        var ruleName = ShortAttributeName(attr);

        var when = GetWhen(attr);
        if (when is not null)
            yield return ($"When of [{ruleName}] on '{prop.Name}'", when,
                MethodCallProbe.GuardStatement(GeneratedCalls.WhenGuard(model, when)), null);

        var unless = GetUnless(attr);
        if (unless is not null)
            yield return ($"Unless of [{ruleName}] on '{prop.Name}'", unless,
                MethodCallProbe.GuardStatement(GeneratedCalls.UnlessGuard(model, unless)), null);

        if (IsMust(attr))
        {
            // An empty or null name is reported like any other that does not compile. An argument
            // that is not a constant at all is already a compile error in the model's own source.
            if (NameArgument(attr) is { } predicate)
            {
                yield return ($"[{ruleName}] on '{prop.Name}'", predicate,
                    MethodCallProbe.ConditionStatement(
                        GeneratedCalls.MustCondition(model, predicate, GeneratedCalls.RawPropertyAccess(model, prop))),
                    prop.Type);
            }
        }
    }

    /// <summary>
    /// Every call to a model method the validator for <paramref name="classSymbol"/> can make,
    /// as <see cref="MethodCallProbe"/> compiles it: the <c>[Must]</c>, <c>When</c> and
    /// <c>Unless</c> calls of its rules, its <c>[SkipWhen]</c> call and its
    /// <c>[CustomValidation]</c> calls. Empty for a model whose rules name no methods.
    /// </summary>
    public static IEnumerable<(string Name, string Statement, bool Certain)> ProbeCalls(INamedTypeSymbol classSymbol, Compilation compilation)
    {
        foreach (var member in MemberWalker.GetMembersIncludingBase(classSymbol, compilation))
        {
            if (member is not IPropertySymbol prop) continue;
            foreach (var attr in prop.GetAttributes())
            {
                if (!IsRuleAttribute(attr)) continue;
                foreach (var (_, name, statement, argumentType) in MethodCallsOf(attr, prop))
                    yield return (name, statement, CertainCall.Condition(compilation, classSymbol, name, argumentType) is not null);
            }
        }

        if (SkipWhenName(classSymbol) is { } skipWhen)
            yield return (skipWhen.Name, SkipWhenStatement(skipWhen.Name),
                CertainCall.Condition(compilation, classSymbol, skipWhen.Name, argumentType: null) is not null);

        foreach (var (method, _) in CustomValidationMethods(classSymbol, compilation))
            yield return (method.Name, CustomValidationStatement(method), CertainCall.CustomValidation(compilation, classSymbol, method));
    }

    /// <summary>
    /// How <see cref="MethodCallProbe.CallWarnings"/> finds the warnings on the calls the
    /// validator for <paramref name="classSymbol"/> makes. <see cref="CertainCall"/> clears the
    /// calls that cannot warn: a plain <c>[Must]</c> predicate or custom rule whose parameter
    /// takes the property's type exactly, a guard or <c>[SkipWhen]</c> method without
    /// attributes, and a plain <c>[CustomValidation]</c> method. A predicate or custom rule after
    /// a rule that tests the same property for null, such as <c>[NotNull]</c>, is not cleared:
    /// that test leaves the property maybe-null.
    /// <para>
    /// Of the calls that are not cleared, only a predicate or custom rule, which takes the
    /// property's value, or a rule's read of the property can warn differently depending on the
    /// code before it, so only they need the whole body compiled: <see cref="CallWarningProbe.Body"/>.
    /// A guard, <c>[SkipWhen]</c> or <c>[CustomValidation]</c> call takes no argument, and an
    /// attribute on a guard can only remove a nullability warning from a later call, never add
    /// one, so the per-call probe's warnings are its warnings: <see cref="CallWarningProbe.Calls"/>,
    /// issue #256. A <c>[CustomValidation]</c> call made through a cast is the exception, since
    /// the per-call probe compiles it on the model.
    /// </para>
    /// </summary>
    public static CallWarningProbe CallWarningProbeFor(INamedTypeSymbol classSymbol, Compilation compilation)
    {
        bool noArgumentCallMayWarn = false;
        foreach (var member in MemberWalker.GetMembersIncludingBase(classSymbol, compilation))
        {
            // A property that is [Obsolete(error: true)] gets no rule emitted for it at all: its
            // condition becomes the literal false, so nothing about it is ever probed.
            if (member is not IPropertySymbol prop || ObsoleteErrors.IsObsoleteError(prop)) continue;

            // Only [Must] and custom rules emit no null test of their own; every other rule may.
            bool nullTested = false;
            foreach (var attr in prop.GetAttributes())
            {
                if (!IsRuleAttribute(attr)) continue;
                foreach (var (_, name, _, argumentType) in MethodCallsOf(attr, prop))
                {
                    var argument = argumentType is null ? null : prop;
                    if (CertainCall.ConditionCannotWarn(compilation, classSymbol, name, argument, nullTested)) continue;
                    if (argument is not null) return CallWarningProbe.Body;
                    noArgumentCallMayWarn = true;
                }

                bool isCustomRule = CustomRules.TryGetRuleValueType(attr.AttributeClass!, out var valueType);
                if (isCustomRule && !CertainCall.RuleCallCannotWarn(attr.AttributeClass!, valueType, prop, nullTested))
                    return CallWarningProbe.Body;
                if (!isCustomRule && !IsMust(attr))
                {
                    nullTested = true;
                    // A plain built-in rule makes no call of its own, but its condition still
                    // reads the property directly, e.g. "instance.Code is null", and that read
                    // alone can warn just as an argument or a guard's can.
                    if (CertainCall.ReadMayWarn(prop)) return CallWarningProbe.Body;
                }
            }
        }

        if (SkipWhenName(classSymbol) is { } skipWhen
            && !CertainCall.ConditionCannotWarn(compilation, classSymbol, skipWhen.Name, argument: null, argumentNullTested: false))
            noArgumentCallMayWarn = true;

        foreach (var (method, _) in CustomValidationMethods(classSymbol, compilation))
        {
            if (CertainCall.CustomValidation(compilation, classSymbol, method)) continue;
            if (CustomValidationReceiver(compilation, classSymbol, method) is not null) return CallWarningProbe.Body;
            noArgumentCallMayWarn = true;
        }
        return noArgumentCallMayWarn ? CallWarningProbe.Calls : CallWarningProbe.None;
    }

    /// <summary>
    /// Writes a probe class for <paramref name="classSymbol"/> into <paramref name="sb"/>: the
    /// generated validator's <c>Validate</c> body, emitted by <see cref="EmitValidateBody"/> as
    /// the generated file has it, with the fields that body reads. Returns where each call is in
    /// <paramref name="sb"/>, by line, as <see cref="CallLineWriter"/> numbers them.
    /// </summary>
    public static IReadOnlyList<List<(Microsoft.CodeAnalysis.Text.TextSpan Span, CallSite Site)>> EmitWarningProbe(
        StringBuilder sb, INamedTypeSymbol classSymbol, Compilation compilation, string className)
    {
        var ns = GeneratedCalls.NamespaceOf(classSymbol);
        if (ns is not null) sb.AppendLine($"namespace {ns}").AppendLine("{");
        // Generic over the generic model's type parameters, as the generated validator is.
        var typeParameters = GenericSignature.TypeParameters(classSymbol);
        sb.AppendLine($"internal sealed class {className}{GenericSignature.ParameterList(typeParameters)}");
        var clauses = GenericSignature.ConstraintClauses(typeParameters);
        for (var i = 0; i < clauses.Count; i++)
            sb.AppendLine($"    {clauses[i]}");
        sb.AppendLine("{");
        AppendNestedValidatorFields(sb, CollectNestedValidatorFields(classSymbol, compilation));
        // A model that must validate asynchronously gets an awaiting body, compiled as the async
        // method the generated validator declares it in.
        var model = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        sb.AppendLine(RequiresAsync(classSymbol, compilation)
            ? $"    private async {AsyncResultType} Validate({model} {MethodCallProbe.Model}, global::System.Threading.CancellationToken {GeneratedCalls.CancellationToken})"
            : $"    private global::ZeroAlloc.Validation.ValidationResult Validate({model} {MethodCallProbe.Model})");
        sb.AppendLine("    {");
        var fields = new GeneratedFields();
        var calls = CallLineWriter.Recording();
        EmitValidateBody(sb, classSymbol, compilation, MethodCallProbe.Model, fields, calls);
        sb.AppendLine("    }");
        fields.AppendDeclarations(sb);
        sb.AppendLine("}");
        if (ns is not null) sb.AppendLine("}");
        return calls.Recorded;
    }

    /// <summary>
    /// The calls on one rule's condition line, in the order they appear on it: its <c>When</c>
    /// and <c>Unless</c> guards, then its <c>[Must]</c> predicate or custom rule call, or, for a
    /// plain built-in rule that makes none of those, the condition itself — it still reads the
    /// property directly, e.g. <c>instance.Code is null</c>, and that read alone can warn. The
    /// whole condition is used, rather than just the property access within it, so a condition
    /// that reads the property more than once, such as a length check's null guard, is covered
    /// by one site and not matched piecemeal. The usages read as <see cref="MethodCallsOf"/>
    /// writes them.
    /// </summary>
    private static List<CallSite> RuleCallSites(
        AttributeData attr, IPropertySymbol prop, string modelParamName, string rawAccess, int ruleIndex, string condition)
    {
        var rule = ShortAttributeName(attr);
        var declaringType = prop.ContainingType;
        var sites = new List<CallSite>(1);

        if (GetWhen(attr) is { } when)
            sites.Add(new CallSite(GeneratedCalls.MethodCall(modelParamName, when), attr, prop, declaringType, $"When of [{rule}] on '{prop.Name}'"));
        if (GetUnless(attr) is { } unless)
            sites.Add(new CallSite(GeneratedCalls.MethodCall(modelParamName, unless), attr, prop, declaringType, $"Unless of [{rule}] on '{prop.Name}'"));

        if (CustomRules.TryGetRuleValueType(attr.AttributeClass!, out _, out var isAsyncRule))
        {
            var field = CustomRules.FieldName(prop.Name, ruleIndex);
            sites.Add(new CallSite(isAsyncRule ? GeneratedCalls.AsyncRuleCall(field, rawAccess) : GeneratedCalls.RuleCall(field, rawAccess),
                attr, prop, declaringType, $"[{rule}] on '{prop.Name}'"));
        }
        else if (IsMust(attr))
        {
            sites.Add(new CallSite(GeneratedCalls.MethodCall(modelParamName, GetStringArg(attr, 0), rawAccess),
                attr, prop, declaringType, $"[{rule}] on '{prop.Name}'"));
        }
        else if (CertainCall.ReadMayWarn(prop))
        {
            sites.Add(new CallSite(condition, attr, prop, declaringType, $"[{rule}] on '{prop.Name}'"));
        }
        return sites;
    }

    /// <summary>
    /// Every rule of <paramref name="classSymbol"/> whose property
    /// <see cref="ObsoleteErrors.IsObsoleteError"/> finds <c>[Obsolete(error: true)]</c>: pragma
    /// cannot suppress the CS0619 a read of it would raise, unlike CS0612 and CS0618, so
    /// <see cref="EmitPropertyRulesForProp"/> and <see cref="EmitFlatPathPropertyRules"/> never
    /// emit a rule for it at all, and this is reported as ZV0032 instead, an error, at the
    /// rule's attribute.
    /// </summary>
    public static IEnumerable<(AttributeData Attribute, IPropertySymbol Property, string RawAccess, string Usage)> ObsoleteErrorRules(
        INamedTypeSymbol classSymbol, Compilation compilation)
    {
        foreach (var member in MemberWalker.GetMembersIncludingBase(classSymbol, compilation))
        {
            if (member is not IPropertySymbol prop || !ObsoleteErrors.IsObsoleteError(prop)) continue;

            foreach (var attr in prop.GetAttributes())
            {
                if (!IsRuleAttribute(attr)) continue;
                var rule = ShortAttributeName(attr);
                yield return (attr, prop, GeneratedCalls.RawPropertyAccess(MethodCallProbe.Model, prop), $"[{rule}] on '{prop.Name}'");
            }
        }
    }

    /// <summary>
    /// Every nested or collection property of <paramref name="classSymbol"/> that
    /// <see cref="ObsoleteErrors.IsObsoleteError"/> finds <c>[Obsolete(error: true)]</c>. The
    /// generated validator would read it to hand it to the nested validator, and pragma cannot
    /// suppress the CS0619 that read raises, so <see cref="GetNestedValidateProperties"/> and
    /// <see cref="GetCollectionValidateProperties"/> leave it out, validator field and
    /// constructor parameter included, and this is reported as ZV0032 instead, an error, at the
    /// property, issue #267. A rule on the same property is reported by
    /// <see cref="ObsoleteErrorRules"/>, at the rule's attribute.
    /// </summary>
    public static IEnumerable<(IPropertySymbol Property, string RawAccess, string Usage)> ObsoleteErrorNestedReads(
        INamedTypeSymbol classSymbol, Compilation compilation)
    {
        foreach (var prop in NestedValidateCandidates(classSymbol, compilation))
        {
            if (ObsoleteErrors.IsObsoleteError(prop))
                yield return (prop, GeneratedCalls.RawPropertyAccess(MethodCallProbe.Model, prop), $"nested validation of '{prop.Name}'");
        }
        foreach (var (prop, _) in CollectionValidateCandidates(classSymbol, compilation))
        {
            if (ObsoleteErrors.IsObsoleteError(prop))
                yield return (prop, GeneratedCalls.RawPropertyAccess(MethodCallProbe.Model, prop), $"collection validation of '{prop.Name}'");
        }
    }

    private static CallSite SkipWhenSite(INamedTypeSymbol classSymbol, in ResolvedMethodCall skipWhen, string modelParamName) =>
        new(GeneratedCalls.SkipWhenCondition(modelParamName, skipWhen.MethodName), skipWhen.Attribute, classSymbol, null, skipWhen.Usage);

    private static CallSite CustomValidationSite(in CustomValidationCall call, string text)
    {
        var attr = FindCustomValidationAttribute(call.Method, out var declaration)!;
        return new CallSite(text, attr, call.Method, declaration.ContainingType, $"[CustomValidation] on '{call.Method.Name}'");
    }

    private static bool IsMust(AttributeData attr) =>
        string.Equals(attr.AttributeClass?.ToDisplayString(), MustFqn, StringComparison.Ordinal);

    private static string ShortAttributeName(AttributeData attr)
    {
        const string suffix = "Attribute";
        var name = attr.AttributeClass?.Name ?? "";
        return name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name.Substring(0, name.Length - suffix.Length)
            : name;
    }

    private static string? GetWhen(AttributeData attr) =>
        TryGetBaseMemberArgument(attr, "When", out var value) ? value.Value as string : null;

    private static string? GetUnless(AttributeData attr) =>
        TryGetBaseMemberArgument(attr, "Unless", out var value) ? value.Value as string : null;

    private static string? GetDisplayName(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (!string.Equals(attr.AttributeClass?.ToDisplayString(), DisplayNameAttributeFqn, StringComparison.Ordinal))
                continue;
            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string s)
                return s;
        }
        return null;
    }

    /// <summary>
    /// The model's <c>[SkipWhen]</c> call, resolved as <c>instance.Method()</c>, or
    /// <see langword="null"/> when the model has no <c>[SkipWhen]</c>. The attribute is read from
    /// the model only, not inherited from a base type.
    /// </summary>
    public static ResolvedMethodCall? ResolveSkipWhen(Compilation compilation, INamedTypeSymbol classSymbol)
    {
        if (SkipWhenName(classSymbol) is not { } skipWhen) return null;
        return new ResolvedMethodCall(skipWhen.Attribute, $"[SkipWhen] on '{classSymbol.Name}'", skipWhen.Name,
            MethodCallProbe.ResolveCondition(compilation, classSymbol, skipWhen.Name, SkipWhenStatement(skipWhen.Name), argumentType: null));
    }

    private static (AttributeData Attribute, string Name)? SkipWhenName(INamedTypeSymbol classSymbol)
    {
        foreach (var attr in classSymbol.GetAttributes())
        {
            if (!string.Equals(attr.AttributeClass?.ToDisplayString(), SkipWhenAttributeFqn, StringComparison.Ordinal))
                continue;
            return NameArgument(attr) is { } name ? (attr, name) : null;
        }
        return null;
    }

    /// <summary>
    /// The method name an attribute's first constructor argument gives: the string, or empty
    /// for <c>null</c>, which is then reported rather than dropped. <see langword="null"/> when
    /// the argument is missing or does not compile, which the compiler already reports.
    /// </summary>
    private static string? NameArgument(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length == 0) return null;
        var argument = attr.ConstructorArguments[0];
        if (argument.Kind == TypedConstantKind.Error) return null;
        return argument.Value as string ?? "";
    }

    private static string SkipWhenStatement(string name) =>
        MethodCallProbe.ConditionStatement(GeneratedCalls.SkipWhenCondition(MethodCallProbe.Model, name));

    /// <summary>
    /// The error code for one usage. An <c>ErrorCode</c> written on the usage wins, and an
    /// explicit <c>ErrorCode = null</c> clears the code rather than falling back. Otherwise a
    /// custom rule takes the code its <c>[RuleMessage]</c> declares, passed in as
    /// <paramref name="ruleMessage"/>.
    /// </summary>
    private static string? GetErrorCode(AttributeData attr, (string Message, string? ErrorCode)? ruleMessage) =>
        TryGetBaseMemberArgument(attr, "ErrorCode", out var value)
            ? value.Value as string
            : ruleMessage?.ErrorCode;

    // Returns 0 = Error (default), 1 = Warning, 2 = Info.
    private static int GetSeverityValue(AttributeData attr) =>
        TryGetBaseMemberArgument(attr, "Severity", out var value) && value.Value is int i ? i : 0;

    private static bool GetBoolNamedArg(AttributeData? attr, string name)
    {
        if (attr is null) return false;
        foreach (var named in attr.NamedArguments)
            if (string.Equals(named.Key, name, StringComparison.Ordinal) && named.Value.Value is bool b)
                return b;
        return false;
    }

    private static string SeverityToLiteral(int severityValue) => severityValue switch
    {
        1 => "global::ZeroAlloc.Validation.Severity.Warning",
        2 => "global::ZeroAlloc.Validation.Severity.Info",
        _ => "global::ZeroAlloc.Validation.Severity.Error"
    };

    private static string BuildFailureInitializer(
        string propName,
        MessageTemplate message,
        AttributeData attr,
        (string Message, string? ErrorCode)? ruleMessage,
        string? propertyValueExpr)
    {
        var errorCode = GetErrorCode(attr, ruleMessage);
        var severityValue = GetSeverityValue(attr);

        string errorMessageExpr;
        if (propertyValueExpr is not null)
        {
            // Escape the literal parts for an interpolated string and join them with the value hole.
            // The parts were split at each {PropertyValue} of the template before any substitution,
            // so a {PropertyValue} inside a substituted value stays literal text.
            var parts = message.Parts;
            var msgSb = new StringBuilder("$\"");
            for (int i = 0; i < parts.Count; i++)
            {
                msgSb.Append(EscapeStringForInterpolation(parts[i]));
                if (i < parts.Count - 1)
                {
                    msgSb.Append('{');
                    msgSb.Append(propertyValueExpr);
                    msgSb.Append('}');
                }
            }
            msgSb.Append('"');
            errorMessageExpr = msgSb.ToString();
        }
        else
        {
            errorMessageExpr = $"\"{EscapeString(message.Parts[0])}\"";
        }

        var sb = new StringBuilder();
        sb.Append($"new global::ZeroAlloc.Validation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = {errorMessageExpr}");
        if (errorCode is not null)
            sb.Append($", ErrorCode = \"{EscapeString(errorCode)}\"");
        if (severityValue != 0)
            sb.Append($", Severity = {SeverityToLiteral(severityValue)}");
        sb.Append(" }");
        return sb.ToString();
    }

    private static object? GetArg(AttributeData attr, int index)
    {
        if (attr.ConstructorArguments.Length <= index) return null;
        return attr.ConstructorArguments[index].Value;
    }

    private static int GetIntArg(AttributeData attr, int index)
        => System.Convert.ToInt32(GetArg(attr, index), CultureInfo.InvariantCulture);

    private static double GetDoubleArg(AttributeData attr, int index)
        => System.Convert.ToDouble(GetArg(attr, index), CultureInfo.InvariantCulture);

    private static string GetStringArg(AttributeData attr, int index)
        => GetArg(attr, index) as string ?? string.Empty;

    private static string GetTypeArgFullName(AttributeData attr, int index)
    {
        if (attr.ConstructorArguments.Length <= index) return "global::System.Enum";
        var typeSymbol = attr.ConstructorArguments[index].Value as ITypeSymbol;
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Enum";
    }

    private static bool IsStringArg(AttributeData attr, int index)
    {
        if (attr.ConstructorArguments.Length <= index) return false;
        return attr.ConstructorArguments[index].Type?.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_String;
    }

    private static string BuildCondition(string fqn, AttributeData attr, string access, string propTypeFullName = "", string modelParamName = "instance", ITypeSymbol? propType = null, string? rawAccess = null, string propName = "", int ruleIndex = 0, GeneratedFields? fields = null)
    {
        // Predicate-style validators (e.g. [Must]) pass the property value as an argument
        // to a user-defined method whose parameter type matches the declared property type.
        // For value-object properties, the access string is unwrapped (e.g. instance.Id.Value),
        // which is correct for built-in operand validators (GreaterThan, NotEmpty, ...) but
        // wrong for predicates — the user's method expects the wrapper. Predicate branches
        // therefore use rawAccess (the un-unwrapped form) when provided.
        var rawForPredicate = rawAccess ?? access;

        // User-defined rules ([NotBlank] deriving from ValidationAttribute<T>) are rebuilt once as
        // a static field and called directly. Like [Must], they receive the raw property value.
        if (CustomRules.TryGetRuleValueType(attr.AttributeClass!, out _, out var isAsyncRule))
        {
            var field = CustomRules.FieldName(propName, ruleIndex);
            if (fields is not null)
            {
                fields.RuleInstances[field] = new RuleInstanceField(
                    attr.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    CustomRules.BuildInitializer(attr),
                    CustomRules.NamesObsoleteSymbol(attr));
            }
            // An async rule is only ever emitted into an async body, as RequiresAsync counts it.
            return isAsyncRule
                ? "!" + GeneratedCalls.Await(GeneratedCalls.AsyncRuleCall(field, rawForPredicate))
                : "!" + GeneratedCalls.RuleCall(field, rawForPredicate);
        }

        // The type of the value the rule reads: for a single-property [ValueObject] that is the
        // unwrapped member, which access already reads through.
        var valueType = ValueTypeOf(propType);
        var toDouble = ToDouble(valueType);
        return fqn switch
        {
            NotNullFqn               => $"{access} is null",
            NotEmptyFqn              => BuildNotEmptyCondition(access, propType),
            MinLengthFqn             => GuardAgainstNull(access, propType, $"{access}.Length < {GetIntArg(attr, 0)}"),
            MaxLengthFqn             => GuardAgainstNull(access, propType, $"{access}.Length > {GetIntArg(attr, 0)}"),
            GreaterThanFqn           => CompareValue(access, valueType, v => $"{toDouble(v)} <= {Number(attr, 0)}"),
            LessThanFqn              => CompareValue(access, valueType, v => $"{toDouble(v)} >= {Number(attr, 0)}"),
            InclusiveBetweenFqn      => CompareValue(access, valueType, v => $"{toDouble(v)} < {Number(attr, 0)} || {toDouble(v)} > {Number(attr, 1)}"),
            GreaterThanOrEqualToFqn  => CompareValue(access, valueType, v => $"{toDouble(v)} < {Number(attr, 0)}"),
            LessThanOrEqualToFqn     => CompareValue(access, valueType, v => $"{toDouble(v)} > {Number(attr, 0)}"),
            ExclusiveBetweenFqn      => CompareValue(access, valueType, v => $"{toDouble(v)} <= {Number(attr, 0)} || {toDouble(v)} >= {Number(attr, 1)}"),
            LengthFqn                => GuardAgainstNull(access, propType, $"{access}.Length < {GetIntArg(attr, 0)} || {access}.Length > {GetIntArg(attr, 1)}"),
            EmailAddressFqn          => GuardAgainstNull(access, valueType, $"!global::ZeroAlloc.Validation.Internal.EmailValidator.IsValid({access})"),
            MatchesFqn               => GuardAgainstNull(access, valueType, BuildMatchesCondition(access, propName, attr, fields)),
            NullFqn                  => $"{access} is not null",
            EmptyFqn                 => $"!string.IsNullOrEmpty({access})",
            EqualFqn                 => IsStringArg(attr, 0)
                ? GuardAgainstNull(access, valueType, $"{access} != \"{EscapeString(GetStringArg(attr, 0))}\"")
                : CompareValue(access, valueType, v => $"{toDouble(v)} != {Number(attr, 0)}"),
            NotEqualFqn              => IsStringArg(attr, 0)
                ? $"{access} == \"{EscapeString(GetStringArg(attr, 0))}\""
                : CompareValue(access, valueType, v => $"{toDouble(v)} == {Number(attr, 0)}"),
            IsInEnumFqn              => BuildIsInEnumCondition(access, propTypeFullName, propType, valueType),
            IsEnumNameFqn            => GuardAgainstNull(access, valueType, $"!global::System.Enum.IsDefined(typeof({GetTypeArgFullName(attr, 0)}), {access})"),
            PrecisionScaleFqn        => CompareValue(access, valueType, v => $"global::ZeroAlloc.Validation.Internal.DecimalValidator.ExceedsPrecisionScale({v}, {GetIntArg(attr, 0)}, {GetIntArg(attr, 1)})"),
            MustFqn                  => GeneratedCalls.MustCondition(modelParamName, GetStringArg(attr, 0), rawForPredicate),
            _                        => "false"
        };
    }

    private static string BuildMatchesCondition(
        string access,
        string propName,
        AttributeData attr,
        GeneratedFields? fields)
    {
        var fieldName = $"__Regex_{propName}";
        var pattern = GetStringArg(attr, 0);

        // If we have a collector, register the pattern so the emitting class
        // can produce the `private static readonly Regex __Regex_<Prop>`
        // field declaration with RegexOptions.Compiled. Dictionary deduplicates
        // sync+async double-visit (both share a single dictionary at the outer
        // class-emission scope).
        if (fields is not null)
        {
            fields.RegexPatterns[fieldName] = pattern;
        }

        return $"!{fieldName}.IsMatch({access})";
    }

    private static string GetDefaultMessage(string fqn, AttributeData attr, string propName) =>
        fqn switch
        {
            NotNullFqn               => $"{propName} must not be null.",
            NotEmptyFqn              => $"{propName} must not be empty.",
            MinLengthFqn             => $"{propName} must be at least {GetArg(attr, 0)} characters.",
            MaxLengthFqn             => $"{propName} must not exceed {GetArg(attr, 0)} characters.",
            GreaterThanFqn           => $"{propName} must be greater than {GetArg(attr, 0)}.",
            LessThanFqn              => $"{propName} must be less than {GetArg(attr, 0)}.",
            InclusiveBetweenFqn      => $"{propName} must be between {GetArg(attr, 0)} and {GetArg(attr, 1)}.",
            GreaterThanOrEqualToFqn  => $"{propName} must be greater than or equal to {GetArg(attr, 0)}.",
            LessThanOrEqualToFqn     => $"{propName} must be less than or equal to {GetArg(attr, 0)}.",
            ExclusiveBetweenFqn      => $"{propName} must be exclusively between {GetArg(attr, 0)} and {GetArg(attr, 1)}.",
            LengthFqn                => $"{propName} must be between {GetArg(attr, 0)} and {GetArg(attr, 1)} characters.",
            EmailAddressFqn          => $"{propName} must be a valid email address.",
            MatchesFqn               => $"{propName} does not match the required pattern.",
            NullFqn                  => $"{propName} must be null.",
            EmptyFqn                 => $"{propName} must be empty.",
            EqualFqn                 => IsStringArg(attr, 0)
                ? $"{propName} must equal \"{GetStringArg(attr, 0)}\"."
                : $"{propName} must equal {GetArg(attr, 0)}.",
            NotEqualFqn              => IsStringArg(attr, 0)
                ? $"{propName} must not equal \"{GetStringArg(attr, 0)}\"."
                : $"{propName} must not equal {GetArg(attr, 0)}.",
            IsInEnumFqn              => $"{propName} is not a valid value.",
            IsEnumNameFqn            => $"{propName} is not a valid enum name.",
            PrecisionScaleFqn        => $"{propName} must not exceed {GetArg(attr, 0)} digits total with {GetArg(attr, 1)} decimal places.",
            MustFqn                  => InvalidFallbackMessage.Replace("{PropertyName}", propName),
            _                        => InvalidFallbackMessage.Replace("{PropertyName}", propName)
        };

    private static string GetNullableUnwrappedFullTypeName(IPropertySymbol prop)
    {
        var type = prop.Type;
        if (type is INamedTypeSymbol named && named.IsGenericType
            && string.Equals(named.OriginalDefinition.ToDisplayString(), "System.Nullable<T>", StringComparison.Ordinal))
            type = named.TypeArguments[0];
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>
    /// Guards a comparison that dereferences the value, so a null never reaches it. A length rule
    /// says nothing about a missing value — that is <c>[NotEmpty]</c>'s or <c>[NotNull]</c>'s job —
    /// which keeps the two composable and matches FluentValidation, where length validators pass on
    /// null. Without the guard the generated validator threw NullReferenceException on exactly the
    /// input it exists to reject, and tripped CS8602 in any consumer with nullable warnings as
    /// errors. <c>[Equal("text")]</c>, <c>[IsEnumName]</c>, <c>[EmailAddress]</c> and
    /// <c>[Matches]</c> are guarded the same way, as <see cref="CompareValue"/> guards the numeric
    /// comparisons: a null string passes them, and an empty one is still checked. <c>[Matches]</c>
    /// used to match a null as "", so whether null passed depended on the pattern, #280.
    /// </summary>
    private static string GuardAgainstNull(string access, ITypeSymbol? propType, string comparison) =>
        CanBeNull(propType) ? $"{access} is not null && ({comparison})" : comparison;

    /// <summary>
    /// Builds a comparison rule's failure condition, <paramref name="comparison"/> applied to the
    /// value. A comparison constrains a value that is present and says nothing about a missing one:
    /// on a <c>Nullable&lt;T&gt;</c> a null passes and the rule compares <c>.Value</c>. Whether a
    /// missing value is acceptable is <c>[NotNull]</c>'s decision, the split the length rules and
    /// <c>[IsInEnum]</c> follow, and FluentValidation's comparison validators pass on null too.
    /// Passing the <c>Nullable&lt;T&gt;</c> itself picked <c>Convert.ToDouble(object)</c>, which
    /// boxed on every call and read null as 0, so <c>[GreaterThan(0)]</c> rejected a missing value
    /// while <c>[LessThan(5)]</c> accepted it, #276. A reference type the conversion accepts, such
    /// as <c>string</c>, is guarded the same way: <c>Convert.ToDouble((string)null)</c> is 0 too.
    /// </summary>
    private static string CompareValue(string access, ITypeSymbol? valueType, Func<string, string> comparison) =>
        valueType?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? $"{access}.HasValue && ({comparison($"{access}.Value")})"
            : GuardAgainstNull(access, valueType, comparison(access));

    private static string Number(AttributeData attr, int index) =>
        GetDoubleArg(attr, index).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// How a numeric comparison converts the value it compares to <c>double</c>:
    /// <c>System.Convert.ToDouble(v)</c>, or for a type parameter constrained to
    /// <c>INumberBase&lt;T&gt;</c>, which <see cref="CanCompareAsNumber"/> requires,
    /// <c>double.CreateChecked(v)</c>, a constrained call that neither boxes nor allocates for a
    /// value-type closing, issue #238.
    /// </summary>
    private static Func<string, string> ToDouble(ITypeSymbol? valueType) =>
        Operand(valueType) is ITypeParameterSymbol
            ? static v => $"double.CreateChecked({v})"
            : static v => $"System.Convert.ToDouble({v})";

    /// <summary>
    /// The type a rule reads the value as: <paramref name="valueType"/>, with a
    /// <c>Nullable&lt;T&gt;</c> unwrapped to its <c>T</c>.
    /// </summary>
    private static ITypeSymbol? Operand(ITypeSymbol? valueType) =>
        valueType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : valueType;

    /// <summary>
    /// The type of the value a built-in rule reads. For a single-property <c>[ValueObject]</c> the
    /// rule reads the unwrapped member, see <see cref="BuildPropertyAccess"/>, so that member's type.
    /// </summary>
    private static ITypeSymbol? ValueTypeOf(ITypeSymbol? propType)
    {
        if (propType is null) return null;
        var member = GetValueObjectUnwrapMember(propType);
        if (member is null) return propType;
        return propType.GetMembers(member).OfType<IPropertySymbol>().FirstOrDefault()?.Type ?? propType;
    }

    /// <summary>
    /// <c>[IsInEnum]</c> checks that a present value is a defined member. On a nullable enum it
    /// checks the underlying value and says nothing about null — that is <c>[NotNull]</c>'s job,
    /// the same split the length rules follow. Passing the <c>Nullable&lt;T&gt;</c> itself to
    /// <c>Enum.IsDefined</c> tripped CS8604, and a null value threw ArgumentNullException.
    /// </summary>
    private static string BuildIsInEnumCondition(string access, string enumTypeFullName, ITypeSymbol? propType, ITypeSymbol? valueType)
    {
        // A type parameter constrained to struct, Enum, which HasTypeParameterForm requires, is
        // checked with the generic overload, compiled per closing without boxing, issue #238. It
        // is the value the rule reads, access, so a generic value object's member is checked.
        if (Operand(valueType) is ITypeParameterSymbol parameter)
        {
            var isDefined = $"global::System.Enum.IsDefined<{parameter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";
            return valueType!.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? $"{access}.HasValue && !{isDefined}({access}.Value)"
                : $"!{isDefined}({access})";
        }

        return propType?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? $"{access}.HasValue && !global::System.Enum.IsDefined(typeof({enumTypeFullName}), {access}.Value)"
            : $"!global::System.Enum.IsDefined(typeof({enumTypeFullName}), {access})";
    }

    /// <summary>
    /// Whether the value could be null at runtime, and so needs guarding before a dereference.
    /// Non-nullable value types cannot, and guarding one would not compile. A type parameter
    /// without a <c>struct</c> or <c>unmanaged</c> constraint can: it may be closed over a
    /// nullable reference type, whatever its constraints say, issue #238.
    /// </summary>
    private static bool CanBeNull(ITypeSymbol? propType) =>
        propType is not null
        && (propType.IsReferenceType
            || propType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            || IsPossiblyNullTypeParameter(propType));

    /// <summary>
    /// Whether <paramref name="type"/> is a type parameter a closing may fill with a reference type
    /// or <c>Nullable&lt;T&gt;</c>, so a value of it may be null: one without a <c>struct</c> or
    /// <c>unmanaged</c> constraint.
    /// </summary>
    private static bool IsPossiblyNullTypeParameter(ITypeSymbol type) =>
        type is ITypeParameterSymbol { HasValueTypeConstraint: false, HasUnmanagedTypeConstraint: false };

    /// <summary>
    /// Emits the right "is empty" predicate for the property's actual type.
    /// Supports strings, Guid, arrays, Span/Memory family, ICollection / IReadOnlyCollection
    /// (covering List, HashSet, Dictionary, etc.), and falls back to IEnumerable + LINQ Any
    /// for non-counting sequences. Without a type symbol (e.g. legacy call site) falls back
    /// to the string-only check so existing behavior is preserved.
    /// </summary>
    private static string BuildNotEmptyCondition(string access, ITypeSymbol? propType)
    {
        if (propType is null)
            return $"string.IsNullOrEmpty({access})";

        // Nullable<T>: [NotEmpty] on T? treats both null AND empty-underlying as "empty".
        // We emit "(!access.HasValue || <inner check on access.Value>)".
        if (propType is INamedTypeSymbol named && named.IsGenericType
            && string.Equals(named.OriginalDefinition.ToDisplayString(), "System.Nullable<T>", StringComparison.Ordinal))
        {
            var inner = BuildNotEmptyCondition($"{access}.Value", named.TypeArguments[0]);
            return $"(!{access}.HasValue || {inner})";
        }

        // string — the original behavior.
        if (propType.SpecialType == SpecialType.System_String)
            return $"string.IsNullOrEmpty({access})";

        // Guid — "empty" means Guid.Empty.
        if (string.Equals(propType.ToDisplayString(), "System.Guid", StringComparison.Ordinal))
            return $"{access} == global::System.Guid.Empty";

        // Arrays — T[] has Length; null-check then check.
        if (propType is IArrayTypeSymbol)
            return $"({access} is null || {access}.Length == 0)";

        // Span<T> / ReadOnlySpan<T> / Memory<T> / ReadOnlyMemory<T> — value types with .IsEmpty.
        var originalDef = propType.OriginalDefinition.ToDisplayString();
        if (originalDef is "System.Span<T>"
            or "System.ReadOnlySpan<T>"
            or "System.Memory<T>"
            or "System.ReadOnlyMemory<T>")
        {
            return $"{access}.IsEmpty";
        }

        // ICollection / ICollection<T> / IReadOnlyCollection<T> — covers List<T>, HashSet<T>,
        // Dictionary<K,V>, Queue<T>, Stack<T>, etc. via the .Count property.
        if (HasCountProperty(propType))
            return $"({access} is null || {access}.Count == 0)";

        // IEnumerable / IEnumerable<T> — no Count, fall back to LINQ Any.
        if (ImplementsEnumerable(propType))
            return $"({access} is null || !global::System.Linq.Enumerable.Any({access}))";

        // Unknown type — preserve old behavior so we don't silently break existing code.
        return $"string.IsNullOrEmpty({access})";
    }

    private static bool HasCountProperty(ITypeSymbol type)
    {
        // Check the type itself + all its interfaces for an int Count property.
        // This catches ICollection (non-generic), ICollection<T>, IReadOnlyCollection<T>,
        // and any custom collection that exposes Count.
        foreach (var member in type.GetMembers("Count"))
        {
            if (member is IPropertySymbol { Type.SpecialType: SpecialType.System_Int32 })
                return true;
        }
        foreach (var iface in type.AllInterfaces)
        {
            foreach (var member in iface.GetMembers("Count"))
            {
                if (member is IPropertySymbol { Type.SpecialType: SpecialType.System_Int32 })
                    return true;
            }
        }
        return false;
    }

    private static bool ImplementsEnumerable(ITypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            var def = iface.OriginalDefinition.ToDisplayString();
            if (def is "System.Collections.IEnumerable" or "System.Collections.Generic.IEnumerable<T>")
                return true;
        }
        return false;
    }

    internal static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string BuildPropertyValueExpr(IPropertySymbol prop, string modelParamName)
    {
        var access = BuildPropertyAccess(modelParamName, prop);
        var type = prop.Type;

        // Nullable value type: int?, double?, etc.
        // Note: global:: alias qualifier is not valid inside C# interpolated string holes,
        // so we use unqualified System.* names here (always available in generated code).
        if (type is INamedTypeSymbol named && named.IsGenericType
            && string.Equals(named.OriginalDefinition.ToDisplayString(), "System.Nullable<T>", StringComparison.Ordinal))
            return $"({access} is null ? \"null\" : System.Convert.ToString({access}.Value, System.Globalization.CultureInfo.InvariantCulture))";

        // Non-nullable value type: int, double, bool, enum, struct, etc.
        // Use Convert.ToString with InvariantCulture to ensure consistent decimal formatting regardless of thread locale.
        if (type.IsValueType)
            return $"System.Convert.ToString({access}, System.Globalization.CultureInfo.InvariantCulture)";

        // string
        if (type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_String)
            return $"{access} ?? \"null\"";

        // A type parameter that may be closed over a value type or a reference type, issue #238:
        // ToString() through ?. would box the one and Convert.ToString(null) returns "" for the
        // other, so null is tested first. This runs on the failure path only.
        if (type is ITypeParameterSymbol)
            return $"({access} is null ? \"null\" : System.Convert.ToString({access}, System.Globalization.CultureInfo.InvariantCulture))";

        // Any other reference type
        return $"{access}?.ToString() ?? \"null\"";
    }

    // Like EscapeString but also escapes { and } for use in a C# interpolated string literal static part.
    private static string EscapeStringForInterpolation(string s) =>
        EscapeString(s).Replace("{", "{{").Replace("}", "}}");

    private static INamedTypeSymbol? GetValidateWithType(IPropertySymbol prop) =>
        ValidatorDependencies.ValidateWithType(prop);

    /// <summary>
    /// The properties the generated validator validates through a nested validator. One that
    /// <see cref="ObsoleteErrors.IsObsoleteError"/> finds <c>[Obsolete(error: true)]</c> is left
    /// out, since pragma cannot suppress the CS0619 its read would raise, and
    /// <see cref="ObsoleteErrorNestedReads"/> reports it as ZV0032 instead, issue #267.
    /// </summary>
    private static IEnumerable<IPropertySymbol> GetNestedValidateProperties(INamedTypeSymbol classSymbol, Compilation compilation) =>
        NestedValidateCandidates(classSymbol, compilation).Where(p => !ObsoleteErrors.IsObsoleteError(p));

    private static IEnumerable<IPropertySymbol> NestedValidateCandidates(INamedTypeSymbol classSymbol, Compilation compilation) =>
        MemberWalker.GetMembersIncludingBase(classSymbol, compilation)
            .OfType<IPropertySymbol>()
            // First arm: type has [Validate] (auto-compose) — also covers the overlap where [ValidateWith] is present on a [Validate] type; [ValidateWith] wins in CollectNestedValidatorFields.
            // Second arm: [ValidateWith] on a non-collection property whose type has no [Validate].
            .Where(p =>
                (HasValidateAttribute(p.Type, compilation) && !ExpandingComposition.IsExpanding(classSymbol, p, compilation))
                || (GetValidateWithType(p) is not null && GetCollectionElementType(p) is null));

    /// <summary>
    /// Whether a value of <paramref name="type"/> is validated by a generated validator this one
    /// can inject, as <see cref="ValidatorDependencies.ComposedModel"/> decides: a <c>[Validate]</c>
    /// model, closings of generic ones included, or a type parameter whose constraint names one. A
    /// <c>[Validate]</c> type the generated validator cannot reach gets none, ZV0025, and neither
    /// does one whose type parameters it cannot redeclare, ZV0029, so a property of that type is
    /// not wired to a validator that does not exist.
    /// </summary>
    private static bool HasValidateAttribute(ITypeSymbol type, Compilation compilation) =>
        ValidatorDependencies.ComposedModel(type, compilation) is not null;

    private static ITypeSymbol? GetCollectionElementType(IPropertySymbol prop) =>
        ValidatorDependencies.CollectionElementType(prop.Type);

    /// <summary>
    /// The properties the generated validator validates element by element. As for
    /// <see cref="GetNestedValidateProperties"/>, an <c>[Obsolete(error: true)]</c> one is left
    /// out and reported by <see cref="ObsoleteErrorNestedReads"/>, issue #267.
    /// </summary>
    private static IEnumerable<(IPropertySymbol Property, INamedTypeSymbol ElementType)> GetCollectionValidateProperties(INamedTypeSymbol classSymbol, Compilation compilation) =>
        CollectionValidateCandidates(classSymbol, compilation).Where(x => !ObsoleteErrors.IsObsoleteError(x.Property));

    private static IEnumerable<(IPropertySymbol Property, INamedTypeSymbol ElementType)> CollectionValidateCandidates(INamedTypeSymbol classSymbol, Compilation compilation) =>
        MemberWalker.GetMembersIncludingBase(classSymbol, compilation)
            .OfType<IPropertySymbol>()
            .Select(p =>
            {
                var element = GetCollectionElementType(p);
                // An element whose type is a type parameter is validated as the [Validate] class
                // its constraint names, issue #238.
                var composed = element is null ? null : ValidatorDependencies.ComposedModel(element, compilation);
                if (composed is not null && !ExpandingComposition.IsExpanding(classSymbol, p, compilation))
                    return ((IPropertySymbol, INamedTypeSymbol)?)(p, composed);
                if (element is INamedTypeSymbol elemType && GetValidateWithType(p) is not null)
                    return (p, elemType);
                return null;
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value);

    /// <summary>
    /// The generated validator's fields and constructor parameters for its nested and collection
    /// properties, with the member each parameter's documentation names; see
    /// <see cref="NestedValidatorFields"/>.
    /// </summary>
    public static System.Collections.Generic.List<(string FieldName, string ParamName, string QualifiedValidatorType, string MemberDescription)>
        CollectNestedValidatorFields(INamedTypeSymbol classSymbol, Compilation compilation)
    {
        var fields = NestedValidatorFields(classSymbol, compilation);
        var result = new System.Collections.Generic.List<(string, string, string, string)>(fields.Count);
        for (var i = 0; i < fields.Count; i++)
            result.Add((fields[i].FieldName, fields[i].ParamName, fields[i].QualifiedValidatorType, fields[i].MemberDescription));
        return result;
    }

    /// <summary>
    /// The validator field each nested or collection property is validated through, as
    /// <see cref="CollectNestedValidatorFields"/> declares it.
    /// </summary>
    private static Dictionary<IPropertySymbol, string> NestedValidatorFieldsByProperty(INamedTypeSymbol classSymbol, Compilation compilation)
    {
        var fields = NestedValidatorFields(classSymbol, compilation);
        var result = new Dictionary<IPropertySymbol, string>(SymbolEqualityComparer.Default);
        for (var i = 0; i < fields.Count; i++)
            result[fields[i].Property] = fields[i].FieldName;
        return result;
    }

    /// <summary>
    /// The field <paramref name="prop"/> is validated through. Every property the emit paths
    /// validate as a nested model or collection is in <paramref name="validatorFields"/>; the
    /// fallback is the name it would have without a collision.
    /// </summary>
    private static string ValidatorField(Dictionary<IPropertySymbol, string> validatorFields, IPropertySymbol prop) =>
        validatorFields.TryGetValue(prop, out var field) ? field : $"_{CamelCase(prop.Name)}Validator";

    /// <summary>
    /// <c>ValidatorFor&lt;TModel&gt;</c> for the nested <c>[Validate]</c> model <paramref name="model"/>,
    /// fully qualified, the type the constructor takes its validator as. Not the model's generated
    /// validator, issue #246: <c>ValidatorFor&lt;TModel&gt;</c> is the service type every generated
    /// validator is registered under, so a container resolves it, and a caller can still pass the
    /// generated validator itself, which converts to it. A closing of a generic model keeps its
    /// type arguments, <c>ValidatorFor&lt;Page&lt;Order&gt;&gt;</c>, issue #238.
    /// </summary>
    private static string ValidatorForParameterType(INamedTypeSymbol model) =>
        $"global::ZeroAlloc.Validation.ValidatorFor<{model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";

    /// <summary>
    /// One validator field and constructor parameter per nested or collection property, in
    /// declaration order. Each is named after the property in camel case, <c>_addressValidator</c>
    /// and <c>addressValidator</c>. Two properties whose names differ only in the case of the first
    /// letter, such as <c>Address</c> and <c>address</c>, would share that name, so the later one
    /// gets the first free numeric suffix, <c>address2</c>, skipping any name another property
    /// already takes. A model without such a pair keeps exactly the names it always had.
    /// The documentation names the member as the camel-cased name with its first letter
    /// capitalised, as it always has; the properties of such a pair are named as declared
    /// instead, since that form would give both the same name.
    /// </summary>
    private static List<(IPropertySymbol Property, string FieldName, string ParamName, string QualifiedValidatorType, string MemberDescription)>
        NestedValidatorFields(INamedTypeSymbol classSymbol, Compilation compilation)
    {
        // ValidatorDependencies decides which properties the constructor takes and as what, so the
        // DI glue registers exactly these, issue #246. A [ValidateWith] validator is taken by its
        // own type, fully qualified so one declared inside another type resolves too.
        var dependencies = ValidatorDependencies.Of(classSymbol, compilation);
        var properties = new List<(IPropertySymbol Property, string Camel, string QualifiedValidatorType)>(dependencies.Count);
        foreach (var (prop, type, isValidateWith, _) in dependencies)
        {
            var qualifiedType = isValidateWith
                ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : ValidatorForParameterType(type);
            properties.Add((prop, CamelCase(prop.Name), qualifiedType));
        }

        var natural = new HashSet<string>(StringComparer.Ordinal);
        var shared = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < properties.Count; i++)
        {
            if (!natural.Add(properties[i].Camel))
                shared.Add(properties[i].Camel);
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(IPropertySymbol, string, string, string, string)>(properties.Count);
        for (var i = 0; i < properties.Count; i++)
        {
            var (prop, camel, qualifiedType) = properties[i];
            var name = camel;
            var description = shared.Contains(camel)
                ? prop.Name
                : char.ToUpperInvariant(camel[0]).ToString(CultureInfo.InvariantCulture) + camel.Substring(1);
            if (!used.Add(name))
            {
                for (var n = 2; ; n++)
                {
                    name = camel + n.ToString(CultureInfo.InvariantCulture);
                    if (!natural.Contains(name) && used.Add(name)) break;
                }
            }
            result.Add((prop, $"_{name}Validator", $"{name}Validator", qualifiedType, description));
        }
        return result;
    }

    /// <summary>
    /// Declares the generated validator's nested and collection validator fields, as
    /// <see cref="CollectNestedValidatorFields"/> lists them. The generated validator and
    /// <see cref="EmitWarningProbe"/> both declare them here, so the probe compiles its body
    /// against exactly the fields the validator has.
    /// </summary>
    public static void AppendNestedValidatorFields(
        StringBuilder sb,
        List<(string FieldName, string ParamName, string QualifiedValidatorType, string MemberDescription)> nestedFields)
    {
        foreach (var (fieldName, _, qualifiedType, _) in nestedFields)
            sb.AppendLine($"    private readonly {qualifiedType} {fieldName};");
    }

    private static string CamelCase(string name) =>
        char.ToLowerInvariant(name[0]).ToString(CultureInfo.InvariantCulture) + name.Substring(1);

    internal static ITypeSymbol? GetCollectionElementTypePublic(IPropertySymbol prop) => GetCollectionElementType(prop);

    /// <summary>
    /// Returns the Validate method body as a string (multi-statement block WITHOUT outer braces),
    /// using <paramref name="modelParamName"/> as the instance variable.
    /// </summary>
    internal static string EmitValidateBodyAsString(INamedTypeSymbol classSymbol, Compilation compilation, string modelParamName, GeneratedFields? fields = null)
    {
        var sb = new System.Text.StringBuilder();
        EmitValidateBody(sb, classSymbol, compilation, modelParamName, fields);
        return sb.ToString();
    }

    /// <summary>
    /// Returns true when an `is not null` guard against a value of this type
    /// would compile and have meaningful runtime semantics. Class types always
    /// need the guard. <c>Nullable&lt;T&gt;</c> keeps it (the guard lowers to
    /// <c>HasValue</c>). Non-nullable value types cannot take the guard (CS0037),
    /// so the generator omits it.
    /// </summary>
    private static bool NeedsNullGuard(ITypeSymbol type) =>
        !type.IsValueType
        || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private const string ValueObjectAttributeFqn = "ZeroAlloc.ValueObjects.ValueObjectAttribute";

    /// <summary>
    /// If <paramref name="type"/> is a single-property value-object (decorated
    /// with <c>[ZeroAlloc.ValueObjects.ValueObject]</c> and declaring exactly one
    /// public instance property), returns that property's name. Returns null for
    /// everything else — class types, primitives, multi-property value-objects,
    /// or types without the marker attribute.
    /// </summary>
    private static string? GetValueObjectUnwrapMember(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return null;

        var hasMarker = named.GetAttributes()
            .Any(a => string.Equals(
                a.AttributeClass?.ToDisplayString(),
                ValueObjectAttributeFqn,
                StringComparison.Ordinal));
        if (!hasMarker) return null;

        var properties = named.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
            .ToArray();

        return properties.Length == 1 ? properties[0].Name : null;
    }

    /// <summary>
    /// True when <paramref name="type"/> carries <c>[ZeroAlloc.ValueObjects.ValueObject]</c>,
    /// regardless of property count. Used by the multi-property diagnostic (ZV0016)
    /// to detect "this is a value-object that auto-unwrap can't help with."
    /// </summary>
    private static bool HasValueObjectAttribute(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            ValueObjectAttributeFqn,
            StringComparison.Ordinal));

    /// <summary>
    /// Builds the access expression for a property's value. When the property's
    /// type is a single-property <c>[ValueObject]</c>, the expression unwraps
    /// through that property (e.g. <c>instance.CustomerId.Value</c>). Otherwise
    /// returns the raw access (<c>instance.CustomerId</c>).
    /// </summary>
    private static string BuildPropertyAccess(string modelParamName, IPropertySymbol prop)
    {
        var raw = GeneratedCalls.RawPropertyAccess(modelParamName, prop);
        var unwrapMember = GetValueObjectUnwrapMember(prop.Type);
        return unwrapMember is not null ? GeneratedCalls.MemberAccess(raw, unwrapMember) : raw;
    }

    private static readonly DiagnosticDescriptor ZV0016 = new DiagnosticDescriptor(
        id: "ZV0016",
        title: "Value-object with multiple properties can't be auto-unwrapped",
        messageFormat:
            "Property '{0}' of type '{1}' carries a built-in validator but '{1}' is a multi-property value-object ({2} properties). " +
            "Auto-unwrap requires exactly one underlying property. " +
            "Either declare the validator on a single-property value-object, or use [Must] / [CustomValidation] with a custom predicate.",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0022 = new DiagnosticDescriptor(
        id: "ZV0022",
        title: "Unknown placeholder in a custom rule message",
        messageFormat: "Placeholder '{0}' in the message for '{1}' on '{2}' does not match any argument; it is emitted literally",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0020 = new DiagnosticDescriptor(
        id: "ZV0020",
        title: "ValidationAttribute subclass the generator cannot emit",
        messageFormat: "'{0}' derives from ValidationAttribute but the generator cannot emit it; derive from ValidationAttribute<T> and override IsValid, or from AsyncValidationAttribute<T> and override IsValidAsync",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0021 = new DiagnosticDescriptor(
        id: "ZV0021",
        title: "Custom rule value type does not match the property type",
        messageFormat: "'{0}' validates '{1}' but property '{2}' is '{3}'{4}",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0023 = new DiagnosticDescriptor(
        id: "ZV0023",
        title: "Custom rule attribute not accessible from the generated validator",
        messageFormat: "'{0}' cannot be emitted: '{1}' is not accessible from the generated validator; make it internal or public",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ZV0033 = new DiagnosticDescriptor(
        id: "ZV0033",
        title: "Numeric comparison rule on a type that is not a number",
        messageFormat: "'{0}' compares '{1}' as a number, but its type '{2}' cannot be converted to one; {3}",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "GreaterThan, GreaterThanOrEqualTo, LessThan, LessThanOrEqualTo, InclusiveBetween, "
            + "ExclusiveBetween, and Equal and NotEqual with a number compare the value as "
            + "System.Convert.ToDouble(value). That works for numbers, enums and other IConvertible "
            + "types such as string and bool, but always throws InvalidCastException for DateTime, "
            + "char, and any type that does not implement IConvertible, such as DateOnly, TimeOnly, "
            + "TimeSpan, DateTimeOffset or Guid. The rule is left out of the generated validator. "
            + "Compare such a value with [Must] or a custom ValidationAttribute<T>. A value whose type "
            + "is a type parameter of a generic model is compared as double.CreateChecked(value), "
            + "which needs the type parameter constrained to System.Numerics.INumberBase<T>, "
            + "directly or through an interface such as INumber<T>.");

    private const string CompareHint = "use [Must] or a custom ValidationAttribute<T> to compare it";

    private const string CompareTypeParameterHint =
        "constrain it to System.Numerics.INumberBase<T>, or use [Must] or a custom ValidationAttribute<T> to compare it";

    private static readonly DiagnosticDescriptor ZV0036 = new DiagnosticDescriptor(
        id: "ZV0036",
        title: "Built-in rule on a value whose type is a type parameter",
        messageFormat: "'{0}' cannot validate '{1}': its type '{2}' is a type parameter, so the rule has no form that fits every closing; constrain the type parameter, use [Must] or a custom ValidationAttribute<T>",
        category: "ZeroAlloc.Validation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A generic model has one generated validator for every closing, so a rule on a property "
            + "whose type is a type parameter must compile for whatever the type parameter is closed "
            + "over. NotEmpty, Empty, MinLength, MaxLength, Length, EmailAddress, Matches, "
            + "IsEnumName, PrecisionScale, and Equal and NotEqual with a string have no such form; "
            + "IsInEnum has one only when the type parameter is constrained to struct, Enum. The "
            + "rule is left out of the generated validator. NotNull, Null and [Must] work on any "
            + "type parameter, the numeric comparisons on one constrained to INumberBase<T>, and a "
            + "custom ValidationAttribute<T> on one that converts to its T.");

    /// <summary>
    /// Whether a rule that is not a numeric comparison, or one over a type
    /// <c>Convert.ToDouble</c> can convert, can be emitted. A numeric comparison over any other
    /// type compiled and then threw InvalidCastException for every value, #279, so it is left
    /// out and, when <paramref name="ctx"/> is set, reported as ZV0033 at the attribute.
    /// </summary>
    private static bool CanCompareAsNumber(Compilation compilation, IPropertySymbol prop, AttributeData attr, DiagnosticSink? ctx)
    {
        if (!IsNumericComparison(attr)) return true;

        // The rule reads the unwrapped member of a single-property value object.
        var valueType = ValueTypeOf(prop.Type) ?? prop.Type;
        if (ConvertsToDouble(compilation, valueType)) return true;

        ctx?.Report(ZV0033, AttributeLocation(attr, prop), attr.AttributeClass!.Name, prop.Name, valueType.ToDisplayString(),
            Operand(valueType) is ITypeParameterSymbol ? CompareTypeParameterHint : CompareHint);
        return false;
    }

    /// <summary>
    /// Whether the rule has a form for its operand, the value it reads after <c>Nullable&lt;T&gt;</c>
    /// and single-property value-object unwrapping, when that is a type parameter of a generic
    /// model, issue #238. The generated validator serves every closing, so the rule must compile
    /// for each: <c>[NotNull]</c>, <c>[Null]</c> and <c>[Must]</c> always do, a custom rule is
    /// checked by ZV0021, and a numeric comparison by ZV0033. <c>[IsInEnum]</c> needs
    /// <c>struct, Enum</c>. Every other built-in rule reads a string, a length or a count, and is
    /// left out and reported as ZV0036 when <paramref name="ctx"/> is set.
    /// </summary>
    private static bool HasTypeParameterForm(IPropertySymbol prop, AttributeData attr, DiagnosticSink? ctx)
    {
        if (Operand(ValueTypeOf(prop.Type)) is not ITypeParameterSymbol parameter) return true;

        var fqn = attr.AttributeClass?.ToDisplayString();
        var supported = fqn switch
        {
            NotNullFqn or NullFqn or MustFqn => true,
            IsInEnumFqn => parameter.HasValueTypeConstraint && HasEnumConstraint(parameter),
            // A number argument makes them numeric comparisons, which ZV0033 checked already.
            EqualFqn or NotEqualFqn => attr.ConstructorArguments.Length == 0 || !IsStringArg(attr, 0),
            GreaterThanFqn or GreaterThanOrEqualToFqn or LessThanFqn or LessThanOrEqualToFqn
                or InclusiveBetweenFqn or ExclusiveBetweenFqn => true,
            _ => CustomRules.IsCustomRule(attr),
        };
        if (supported) return true;

        ctx?.Report(ZV0036, AttributeLocation(attr, prop), attr.AttributeClass!.Name, prop.Name, parameter.ToDisplayString());
        return false;
    }

    private static bool HasEnumConstraint(ITypeParameterSymbol parameter)
    {
        foreach (var constraint in parameter.ConstraintTypes)
        {
            if (constraint.SpecialType == SpecialType.System_Enum) return true;
        }
        return false;
    }

    /// <summary>
    /// The rules <see cref="BuildCondition"/> emits as <c>System.Convert.ToDouble(value)</c>.
    /// <c>[Equal]</c> and <c>[NotEqual]</c> are numeric only with a number argument; with a string
    /// they compare strings. One whose argument did not bind is already a compiler error.
    /// </summary>
    private static bool IsNumericComparison(AttributeData attr) =>
        attr.AttributeClass?.ToDisplayString() switch
        {
            GreaterThanFqn or GreaterThanOrEqualToFqn or LessThanFqn or LessThanOrEqualToFqn
                or InclusiveBetweenFqn or ExclusiveBetweenFqn => true,
            EqualFqn or NotEqualFqn => attr.ConstructorArguments.Length > 0 && !IsStringArg(attr, 0),
            _ => false,
        };

    /// <summary>
    /// Whether <c>System.Convert.ToDouble</c> can convert a value of <paramref name="type"/>, or
    /// of the type a <c>Nullable&lt;T&gt;</c> wraps, which is what the rule compares. Numbers,
    /// including <c>nint</c> and <c>nuint</c>, which bind to the <c>long</c> and <c>ulong</c>
    /// overloads, enums and any other <c>IConvertible</c> type convert. <c>DateTime</c> and
    /// <c>char</c> implement <c>IConvertible</c> but always throw. Every other type binds to
    /// <c>Convert.ToDouble(object)</c>, which throws for a value that is not <c>IConvertible</c>.
    /// An unresolved type is already a compiler error, so it is not reported again.
    /// </summary>
    private static bool ConvertsToDouble(Compilation compilation, ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        // A type parameter has no AllInterfaces of its own, and Convert.ToDouble would box a
        // value-type closing on every call, so only INumberBase<T>, compared through
        // double.CreateChecked, counts, issue #238.
        if (type is ITypeParameterSymbol parameter)
            return IsGenericNumber(parameter);

        switch (type.SpecialType)
        {
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
                return true;
            case SpecialType.System_Char:
            case SpecialType.System_DateTime:
                return false;
        }

        if (type.TypeKind is TypeKind.Enum or TypeKind.Error) return true;

        var convertible = compilation.GetTypeByMetadataName("System.IConvertible");
        return convertible is not null
            && (SymbolEqualityComparer.Default.Equals(type, convertible)
                || type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, convertible)));
    }

    /// <summary>
    /// Whether <paramref name="parameter"/> is constrained to <c>System.Numerics.INumberBase&lt;T&gt;</c>
    /// over itself, directly or through an interface that inherits it, such as <c>INumber&lt;T&gt;</c>,
    /// so <c>double.CreateChecked</c> accepts it.
    /// </summary>
    private static bool IsGenericNumber(ITypeParameterSymbol parameter)
    {
        foreach (var constraint in parameter.ConstraintTypes)
        {
            if (constraint is not INamedTypeSymbol named) continue;
            if (IsNumberBaseOf(named, parameter)) return true;
            foreach (var inherited in named.AllInterfaces)
            {
                if (IsNumberBaseOf(inherited, parameter)) return true;
            }
        }
        return false;
    }

    private static bool IsNumberBaseOf(INamedTypeSymbol type, ITypeParameterSymbol parameter) =>
        type.TypeArguments.Length == 1
        && string.Equals(type.OriginalDefinition.ToDisplayString(), "System.Numerics.INumberBase<TSelf>", StringComparison.Ordinal)
        && SymbolEqualityComparer.Default.Equals(type.TypeArguments[0], parameter);

    /// <summary>
    /// Fires ZV0020 for an attribute deriving from <c>ValidationAttribute</c> that is neither a
    /// built-in rule nor a <c>ValidationAttribute&lt;T&gt;</c> custom rule. The generator has no
    /// way to evaluate it, so without this error the property would silently go unvalidated.
    /// The caller has already established that <paramref name="attr"/> is not a rule attribute.
    /// </summary>
    private static void ReportZV0020IfApplicable(DiagnosticSink? ctx, IPropertySymbol prop, AttributeData attr)
    {
        if (ctx is null) return;
        if (attr.AttributeClass is not { } attrClass || !CustomRules.DerivesFromValidationAttribute(attrClass)) return;

        ctx.Report(ZV0020, AttributeLocation(attr, prop), attrClass.Name);
    }

    /// <summary>
    /// Whether a custom rule usage can be emitted. The rule is left out, and reported when
    /// <paramref name="ctx"/> is set, when its attribute is not reachable from the generated
    /// validator (ZV0023) or when the property type has no implicit conversion to the rule's
    /// <c>T</c> (ZV0021). Either would otherwise surface as a compiler error in generated code.
    /// </summary>
    private static bool CanEmitCustomRule(Compilation compilation, IPropertySymbol prop, AttributeData attr, DiagnosticSink? ctx)
    {
        var attrClass = attr.AttributeClass!;
        CustomRules.TryGetRuleValueType(attrClass, out var valueType);

        var inaccessible = CustomRules.FindInaccessibleSymbol(compilation, attr);
        var accessible = inaccessible is null;

        // An unresolved property type or T is already a compiler error at its declaration, so the
        // rule is skipped without adding a ZV0021 on top of it.
        var unresolved = ContainsErrorType(prop.Type) || ContainsErrorType(valueType);
        var nullMismatch = !unresolved && AcceptsNullRuleDoesNot(prop.Type, valueType);
        var convertible = !unresolved
            && !nullMismatch
            && compilation.ClassifyCommonConversion(prop.Type, valueType).IsImplicit;

        if (ctx is not null)
        {
            var location = AttributeLocation(attr, prop);
            if (inaccessible is not null)
                ctx.Report(ZV0023, location, attrClass.Name, inaccessible.ToDisplayString());
            if (!convertible && !unresolved)
            {
                var hint = nullMismatch
                    ? $". Declare the rule as ValidationAttribute<{valueType.WithNullableAnnotation(NullableAnnotation.Annotated).ToDisplayString()}> to accept null."
                    : "";
                ctx.Report(
                    ZV0021, location,
                    attrClass.Name, valueType.ToDisplayString(), prop.Name, prop.Type.ToDisplayString(), hint);
            }
        }

        return accessible && convertible;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is, or is built from, a type the compiler could not
    /// resolve. <c>Missing?</c> binds as <c>Nullable&lt;Missing&gt;</c>, so the type arguments of
    /// a generic and the element type of an array are searched too.
    /// </summary>
    private static bool ContainsErrorType(ITypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Error } => true,
        IArrayTypeSymbol array => ContainsErrorType(array.ElementType),
        INamedTypeSymbol { IsGenericType: true } named => named.TypeArguments.Any(ContainsErrorType),
        _ => false,
    };

    /// <summary>
    /// Whether a property declared to hold null, such as <c>string?</c>, is checked by a rule
    /// whose <c>T</c> declares it never receives null, such as <c>ValidationAttribute&lt;string&gt;</c>.
    /// Conversion classification ignores nullable annotations, so this is checked separately.
    /// Both annotations exist only where the nullable context is enabled; in an oblivious or
    /// disabled context they are <c>None</c> and this never fires. A property whose type is a type
    /// parameter without a <c>struct</c> or <c>unmanaged</c> constraint holds null whenever it is
    /// closed over a nullable type, so it counts as declared to hold null, issue #238.
    /// </summary>
    private static bool AcceptsNullRuleDoesNot(ITypeSymbol propertyType, ITypeSymbol valueType) =>
        valueType.IsReferenceType
        && valueType.NullableAnnotation == NullableAnnotation.NotAnnotated
        && ((propertyType.IsReferenceType && propertyType.NullableAnnotation == NullableAnnotation.Annotated)
            || (IsPossiblyNullTypeParameter(propertyType) && propertyType.NullableAnnotation != NullableAnnotation.None));

    /// <summary>
    /// Whether <paramref name="attr"/>'s generated condition consumes the unwrapped operand
    /// (<c>access</c> in <see cref="BuildCondition"/>) rather than the raw wrapper
    /// (<c>rawForPredicate</c>). <c>[Must]</c> and custom rules deriving from
    /// <c>ValidationAttribute&lt;T&gt;</c> both receive the raw wrapper and never count; every
    /// other rule reaching here is a built-in operand rule (NotNull, GreaterThan, ...) and does.
    /// </summary>
    private static bool ConsumesUnwrappedValue(AttributeData attr) =>
        !CustomRules.IsCustomRule(attr)
        && !string.Equals(attr.AttributeClass?.ToDisplayString(), MustFqn, StringComparison.Ordinal);

    private static Location AttributeLocation(AttributeData attr, IPropertySymbol prop) =>
        attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            ?? prop.Locations.FirstOrDefault()
            ?? Location.None;

    /// <summary>
    /// Fires ZV0016 when a property has built-in validation rules and its type is a
    /// multi-property <c>[ValueObject]</c> (i.e. has the marker attribute but no
    /// single property to auto-unwrap through). Single-property value-objects are
    /// handled by <see cref="BuildPropertyAccess"/>; primitives/class types are
    /// emitted as-is.
    /// </summary>
    private static void ReportZV0016IfApplicable(DiagnosticSink ctx, IPropertySymbol prop, List<AttributeData> rules)
    {
        // Only a rule that consumes the unwrapped operand (a built-in like NotNull, GreaterThan,
        // ...) needs auto-unwrap to work. [Must] and custom ValidationAttribute<T> rules both
        // receive the raw wrapper via rawForPredicate in BuildCondition and never benefit from
        // it, so a property whose rules are only those never warrants ZV0016.
        if (!rules.Exists(ConsumesUnwrappedValue)) return;
        if (!HasValueObjectAttribute(prop.Type)) return;
        if (GetValueObjectUnwrapMember(prop.Type) is not null) return;

        var propCount = ((INamedTypeSymbol)prop.Type).GetMembers()
            .OfType<IPropertySymbol>()
            .Count(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public);

        ctx.Report(
            ZV0016,
            prop.Locations.FirstOrDefault() ?? Location.None,
            prop.Name, prop.Type.Name, propCount);
    }
}
