using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Asks the compiler whether the calls generated validators make to their models compile. Calls
/// <see cref="CertainCall"/> accepts need nothing. For the rest, one probe file per compilation
/// is added to a copy of it, covering every model: the generated files' header and
/// <c>using</c> directives from <see cref="GeneratedCalls.AppendHeader"/>, each model's
/// namespace, and one method per call holding the statement the validator will contain. The
/// probe's own diagnostics are the verdict, so overload resolution, extension methods,
/// delegates, conversions, type inference and accessibility are exactly the compiler's.
/// <para>
/// The probe sees the generator's input compilation, which does not contain what other source
/// generators add. A call whose member is not found at all may be completed by one of them, so
/// it is not reported: it is emitted, and the final compilation decides, as it always did.
/// </para>
/// </summary>
internal static class MethodCallProbe
{
    /// <summary>The model parameter's name in the generated <c>Validate</c> method.</summary>
    public const string Model = "instance";

    private const string ProbeClass = "__ZeroAllocValidationMethodCallProbe";
    private const string ValidateAttributeFqn = "ZeroAlloc.Validation.ValidateAttribute";

    /// <summary>
    /// Errors that mean the input compilation has no member of that name for the call: another
    /// generator may add one. CS1061 and CS0117 for a missing member, CS0103 for a missing name,
    /// and CS1929 for an extension method of that name that takes another receiver.
    /// </summary>
    private static readonly HashSet<string> MemberNotFound =
        new(System.StringComparer.Ordinal) { "CS1061", "CS0117", "CS0103", "CS1929" };

    /// <summary>
    /// Probe verdicts per compilation, keyed by model and statement. The generator runs every
    /// model against each new compilation, and all of them share this one probe. It is safe
    /// across runs: a <see cref="Compilation"/> is immutable, so a verdict computed for it never
    /// goes stale; any edit produces a new compilation object and so a new entry; and the table
    /// holds its keys weakly, so an entry goes away with its compilation. Models are keyed by
    /// name, not symbol, so a model symbol carried over from an earlier run still finds its
    /// verdicts.
    /// </summary>
    private static readonly ConditionalWeakTable<Compilation, Lazy<ConcurrentDictionary<string, MethodResolution>>> Verdicts =
        new();

    /// <summary>A statement that uses a <c>When</c> or <c>Unless</c> guard as the validator does.</summary>
    public static string GuardStatement(string guard) => $"if ({guard}true) {{ }}";

    /// <summary>A statement that uses a condition as the validator does.</summary>
    public static string ConditionStatement(string condition) => $"if ({condition}) {{ }}";

    /// <summary>A statement that evaluates a call whose result the validator walks.</summary>
    public static string ValueStatement(string value) => $"var __value = {value};";

    /// <summary>
    /// How a <c>[Must]</c>, <c>When</c>, <c>Unless</c> or <c>[SkipWhen]</c> call compiles:
    /// <see cref="CertainCall.Condition"/> accepts the calls that certainly do, and the probe
    /// decides the rest. <paramref name="argumentType"/> is the type of the call's one argument,
    /// or <see langword="null"/> when it takes none.
    /// </summary>
    public static MethodResolution ResolveCondition(
        Compilation compilation, INamedTypeSymbol model, string name, string statement, ITypeSymbol? argumentType)
    {
        if (CertainCall.Condition(compilation, model, name, argumentType) is { } method)
            return new MethodResolution(MethodReach.Callable, method, null);
        return Resolve(compilation, model, name, statement);
    }

    /// <summary>
    /// How <paramref name="statement"/>, one of the statements <see cref="RuleEmitter.ProbeCalls"/>
    /// lists for <paramref name="model"/>, compiles in the generated validator.
    /// <paramref name="name"/> is the method it calls.
    /// </summary>
    public static MethodResolution Resolve(Compilation compilation, INamedTypeSymbol model, string name, string statement)
    {
        if (!GeneratedCalls.IsMethodName(name))
        {
            return new MethodResolution(MethodReach.NotFound, null,
                name.Length == 0 ? "no method name is given" : $"'{name}' is not a method name");
        }

        var verdicts = Verdicts
            .GetValue(compilation, c => new Lazy<ConcurrentDictionary<string, MethodResolution>>(
                () => ProbeAll(c), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

        var key = Key(model, statement);
        if (verdicts.TryGetValue(key, out var verdict)) return verdict;

        // The whole-compilation probe covers every [Validate] type the compilation declares. A
        // model it did not find is probed on its own, once, and stored with the rest.
        foreach (var entry in Probe(compilation, new[] { model }))
            verdicts.TryAdd(entry.Key, entry.Value);
        return verdicts[key];
    }

    private static string Key(INamedTypeSymbol model, string statement) =>
        model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "\n" + statement;

    private static ConcurrentDictionary<string, MethodResolution> ProbeAll(Compilation compilation) =>
        new(Probe(compilation, ValidatedTypes(compilation.Assembly.GlobalNamespace, compilation)), System.StringComparer.Ordinal);

    /// <summary>
    /// The types in <paramref name="ns"/> and below that get a generated validator: declared
    /// <c>[Validate]</c>, not generic (ZV0029), and reachable (ZV0025).
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> ValidatedTypes(INamespaceSymbol ns, Compilation compilation)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol child)
            {
                foreach (var type in ValidatedTypes(child, compilation))
                    yield return type;
            }
            else if (member is INamedTypeSymbol type)
            {
                foreach (var validated in ValidatedTypes(type, compilation))
                    yield return validated;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> ValidatedTypes(INamedTypeSymbol type, Compilation compilation)
    {
        if (!type.IsGenericType && HasValidateAttribute(type) && GeneratedValidatorReach.HasGeneratedValidator(type, compilation))
            yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var validated in ValidatedTypes(nested, compilation))
                yield return validated;
        }
    }

    private static bool HasValidateAttribute(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Compiles every call of <paramref name="models"/> that <see cref="CertainCall"/> does not
    /// accept in one probe file, in one copy of the compilation, and returns the verdicts. No
    /// copy is made when there is nothing to probe.
    /// </summary>
    private static Dictionary<string, MethodResolution> Probe(Compilation compilation, IEnumerable<INamedTypeSymbol> models)
    {
        var verdicts = new Dictionary<string, MethodResolution>(System.StringComparer.Ordinal);
        var probed = CollectCalls(compilation, models, out var byNamespace);
        if (probed.Count == 0) return verdicts;

        var tree = CSharpSyntaxTree.ParseText(ProbeText(probed, byNamespace), FirstParseOptions(compilation));
        var semanticModel = compilation.AddSyntaxTrees(tree).GetSemanticModel(tree);
        var diagnostics = semanticModel.GetDiagnostics();

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is not MethodDeclarationSyntax declaration) continue;
            var call = probed[CallIndex(declaration)];
            verdicts[call.Key] = Verdict(compilation, semanticModel, call.Model, declaration, diagnostics);
        }
        System.Diagnostics.Debug.Assert(verdicts.Count == probed.Count, "Every probed call has a method in the probe file.");
        return verdicts;
    }

    /// <summary>
    /// The calls of <paramref name="models"/> the fast path does not accept, each once, and their
    /// indices grouped by the namespace the model's validator is declared in.
    /// </summary>
    private static List<(string Key, INamedTypeSymbol Model, string Statement)> CollectCalls(
        Compilation compilation, IEnumerable<INamedTypeSymbol> models, out Dictionary<string, List<int>> byNamespace)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var probed = new List<(string Key, INamedTypeSymbol Model, string Statement)>();
        byNamespace = new Dictionary<string, List<int>>(System.StringComparer.Ordinal);

        foreach (var model in models)
        {
            foreach (var (name, statement, certain) in RuleEmitter.ProbeCalls(model, compilation))
            {
                var key = Key(model, statement);
                if (certain || !GeneratedCalls.IsMethodName(name) || !seen.Add(key)) continue;

                var ns = GeneratedCalls.NamespaceOf(model) ?? "";
                if (!byNamespace.TryGetValue(ns, out var indices))
                {
                    indices = new List<int>();
                    byNamespace[ns] = indices;
                }
                indices.Add(probed.Count);
                probed.Add((key, model, statement));
            }
        }
        return probed;
    }

    /// <summary>
    /// The probe file: the generated files' header and <c>using</c> directives, then per model
    /// namespace a class with one method per call, each taking the model as the generated
    /// <c>Validate</c> method does.
    /// </summary>
    private static string ProbeText(
        List<(string Key, INamedTypeSymbol Model, string Statement)> probed, Dictionary<string, List<int>> byNamespace)
    {
        var text = new StringBuilder();
        GeneratedCalls.AppendHeader(text);
        foreach (var group in byNamespace)
        {
            bool global = group.Key.Length == 0;
            if (!global) text.AppendLine($"namespace {group.Key}").AppendLine("{");
            text.AppendLine($"internal static class {ProbeClass}");
            text.AppendLine("{");
            var indices = group.Value;
            for (int i = 0; i < indices.Count; i++)
            {
                var call = probed[indices[i]];
                var modelName = call.Model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                text.AppendLine($"    private static void Call{indices[i].ToString(CultureInfo.InvariantCulture)}({modelName} {Model})");
                text.AppendLine("    {");
                text.AppendLine($"        {call.Statement}");
                text.AppendLine("    }");
            }
            text.AppendLine("}");
            if (!global) text.AppendLine("}");
        }
        return text.ToString();
    }

    private static int CallIndex(MethodDeclarationSyntax declaration) =>
        int.Parse(declaration.Identifier.ValueText.Substring("Call".Length), CultureInfo.InvariantCulture);

    private static CSharpParseOptions? FirstParseOptions(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
            return tree.Options as CSharpParseOptions;
        return null;
    }

    private static MethodResolution Verdict(
        Compilation compilation,
        SemanticModel semanticModel,
        INamedTypeSymbol model,
        MethodDeclarationSyntax declaration,
        System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics)
    {
        InvocationExpressionSyntax? call = null;
        foreach (var node in declaration.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                call = invocation;
                break;
            }
        }

        var symbolInfo = call is null ? default : semanticModel.GetSymbolInfo(call);
        var method = InOriginal(symbolInfo.Symbol as IMethodSymbol ?? FirstMethod(symbolInfo.CandidateSymbols), compilation);

        Diagnostic? error = null;
        foreach (var diagnostic in diagnostics)
        {
            // A warning, even one the project treats as an error, does not stop the call binding.
            if (diagnostic.Severity != DiagnosticSeverity.Error || diagnostic.IsWarningAsError) continue;
            if (!declaration.Span.Contains(diagnostic.Location.SourceSpan)) continue;

            if (diagnostic.Id is "CS0176" or "CS0122")
                return Unreachable(diagnostic.Id, method, model);
            error ??= diagnostic;
        }

        if (error is null)
            return new MethodResolution(MethodReach.Callable, method, null);
        if (MemberNotFound.Contains(error.Id))
            return new MethodResolution(MethodReach.MissingFromInput, null, null);

        return new MethodResolution(MethodReach.NotFound, method,
            $"'{call}' fails with {error.Id}: {error.GetMessage(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// ZV0028's cases: CS0176, a static method, and CS0122, one the validator cannot access. An
    /// inaccessible instance method on a base type is ZV0017's case instead.
    /// </summary>
    private static MethodResolution Unreachable(string id, IMethodSymbol? method, INamedTypeSymbol model)
    {
        if (string.Equals(id, "CS0176", System.StringComparison.Ordinal) || method is { IsStatic: true })
            return new MethodResolution(MethodReach.Static, method, null);
        bool onModel = method is null
            || SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, model.OriginalDefinition);
        return new MethodResolution(onModel ? MethodReach.Inaccessible : MethodReach.InaccessibleOnBase, method, null);
    }

    /// <summary>
    /// The probe compiles a copy of the compilation, whose symbols are not the generator's.
    /// <paramref name="method"/>'s declaration is looked up again in <paramref name="compilation"/>,
    /// so callers can compare it with the symbols they hold.
    /// </summary>
    private static IMethodSymbol? InOriginal(IMethodSymbol? method, Compilation compilation)
    {
        if (method is null) return null;
        var declaration = (method.ReducedFrom ?? method).OriginalDefinition;
        var id = DocumentationCommentId.CreateDeclarationId(declaration);
        return id is null ? null : DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation) as IMethodSymbol;
    }

    private static IMethodSymbol? FirstMethod(System.Collections.Immutable.ImmutableArray<ISymbol> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate is IMethodSymbol method) return method;
        }
        return null;
    }
}
