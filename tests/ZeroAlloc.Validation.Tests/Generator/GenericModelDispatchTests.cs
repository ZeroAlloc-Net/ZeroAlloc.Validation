using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.Validation.AspNetCore.Generator;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Issue #238, phase 3: the ASP.NET Core filter hands every argument its type-switch does not
/// know to a generated <c>ZeroAllocGenericModelDispatch</c>, which looks it up among the
/// registered <c>IModelValidator</c> entries and fails loudly for an unregistered closing of one
/// of this compilation's generic models. This host does not reference ASP.NET Core, so the
/// filter is checked as text; the dispatch class uses no ASP.NET Core type, so it is compiled
/// here, and the whole glue is compiled and run in ZeroAlloc.Validation.Tests.AspNetCore.
/// </summary>
public class GenericModelDispatchTests
{
    private const string FilterHint = "ZeroAlloc.Validation.ZeroAllocValidationActionFilter.g.cs";
    private const string DispatchHint = "ZeroAlloc.Validation.ZeroAllocGenericModelDispatch.g.cs";
    private const string ExtensionsHint = "ZeroAlloc.Validation.ZeroAllocValidationServiceCollectionExtensions.g.cs";

    [Fact]
    public void Filter_HandsUnknownArgumentsToTheDispatch()
    {
        var output = Run("""
            using ZeroAlloc.Validation;
            namespace Ns;
            [Validate] public class Order { [NotEmpty] public string Id { get; set; } = ""; }
            """);

        var filter = output.Source(FilterHint).ReplaceLineEndings("\n");
        Assert.Contains("public ZeroAllocValidationActionFilter(global::System.IServiceProvider services, ZeroAllocGenericModelDispatch generic)", filter, StringComparison.Ordinal);
        Assert.Contains(
            "            case global::Ns.Order order_arg:\n"
            + "                return await _services.GetRequiredService<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Order>>().ValidateAsync(order_arg, ct);\n"
            + "            case null: return null;\n"
            + "            default: return await _generic.ValidateAsync(arg, ct);\n",
            filter,
            StringComparison.Ordinal);
        Assert.Contains("services.TryAddSingleton<ZeroAllocGenericModelDispatch>();", output.Source(ExtensionsHint), StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_WithoutGenericModels_ListsNone_AndCompiles()
    {
        // Emitted always: an application whose own models are not generic may still register
        // closings of a referenced library's generic models, which reach the filter here.
        var output = Run("""
            using ZeroAlloc.Validation;
            namespace Ns;
            [Validate] public class Order { [NotEmpty] public string Id { get; set; } = ""; }
            """);

        Assert.Contains("private static readonly global::System.Type[] GenericModels = [];", output.Source(DispatchHint), StringComparison.Ordinal);
        AssertDispatchCompiles(output);
    }

    // record is a contextual keyword, written as it is; class and event are escaped.
    [Fact]
    public void Dispatch_ListsTheUnboundFormOfEveryGenericModel_AndCompiles()
    {
        var output = Run("""
            using ZeroAlloc.Validation;
            namespace Ns
            {
                [Validate] public class Page<TItem> where TItem : class { [NotEmpty] public string Title { get; set; } = ""; }
                [Validate] public class Pair<TKey, TValue> { [NotEmpty] public string Name { get; set; } = ""; }
                public class Envelope<T>
                {
                    [Validate] public class Header { [NotEmpty] public string Name { get; set; } = ""; }
                    public class Mid { [Validate] internal class Part<U> { [NotEmpty] public string Name { get; set; } = ""; } }
                }
                [Validate] public record Rec<T>([property: NotEmpty] string Name);
            }
            namespace @class.@event { [Validate] public class @record<T> { [NotEmpty] public string Name { get; set; } = ""; } }
            """);

        Assert.Contains(
            "GenericModels = [typeof(global::Ns.Page<>), typeof(global::Ns.Pair<,>), typeof(global::Ns.Envelope<>.Header), "
            + "typeof(global::Ns.Envelope<>.Mid.Part<>), typeof(global::Ns.Rec<>), typeof(global::@class.@event.record<>)];",
            output.Source(DispatchHint),
            StringComparison.Ordinal);
        AssertDispatchCompiles(output);
    }

    [Fact]
    public void ProjectWhoseOnlyModelsAreGeneric_GetsTheFilter()
    {
        var output = Run("""
            using ZeroAlloc.Validation;
            namespace Ns;
            [Validate] public class Page<TItem> where TItem : class { [NotEmpty] public string Title { get; set; } = ""; }
            """);

        var filter = output.Source(FilterHint);
        Assert.Contains("default: return await _generic.ValidateAsync(arg, ct);", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("Page", filter, StringComparison.Ordinal);
        Assert.Contains("AddZeroAllocAspNetCoreValidation(", output.Source(ExtensionsHint), StringComparison.Ordinal);
        AssertDispatchCompiles(output);
    }

    [Fact]
    public void ProjectWithoutModels_GetsNoGlue()
    {
        var output = Run("""
            namespace Ns;
            public class Plain { }
            """);

        Assert.Empty(output.Generated);
    }

    private static GenericModelTestHost.Output Run(string source) =>
        GenericModelTestHost.Run(source, new ValidatorGenerator(), new AspNetCoreFilterEmitter());

    /// <summary>
    /// Compiles the source, the validators and the dispatch class, without the filter and the
    /// extension, which need ASP.NET Core, and asserts no error or warning.
    /// </summary>
    private static void AssertDispatchCompiles(GenericModelTestHost.Output output)
    {
        var aspNetCoreOnly = output.Compilation.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(FilterHint, StringComparison.Ordinal)
                        || t.FilePath.EndsWith(ExtensionsHint, StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, aspNetCoreOnly.Length);

        var problems = output.Compilation.RemoveSyntaxTrees(aspNetCoreOnly).GetDiagnostics()
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
            .ToList();
        Assert.Empty(problems);
    }
}
