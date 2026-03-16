using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Generator;

public class AspNetCoreGeneratorTests
{
    [Fact]
    public void Generator_EmitsDispatch_ForBothValidateModels()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            [Validate] public class Order    { [NotEmpty] public string Ref  { get; set; } = ""; }
            """;

        var filter = RunAspNetGeneratorGetSource(source, "ZValidationActionFilter.g.cs");
        Assert.Contains("global::MyApp.Customer", filter, StringComparison.Ordinal);
        Assert.Contains("global::MyApp.Order",    filter, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsExtensionMethod_WithTryAddTransient_ForBothValidators()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            [Validate] public class Order    { [NotEmpty] public string Ref  { get; set; } = ""; }
            """;

        var ext = RunAspNetGeneratorGetSource(source, "ZValidationServiceCollectionExtensions.g.cs");
        Assert.Contains("TryAddTransient<global::MyApp.CustomerValidator>", ext, StringComparison.Ordinal);
        Assert.Contains("TryAddTransient<global::MyApp.OrderValidator>",    ext, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NonValidateType_NotPresentInDispatch()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            public class NotAModel { public string X { get; set; } = ""; }
            """;

        var filter = RunAspNetGeneratorGetSource(source, "ZValidationActionFilter.g.cs");
        Assert.DoesNotContain("NotAModel", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsAddZValidationAutoValidation_ExtensionMethod()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var ext = RunAspNetGeneratorGetSource(source, "ZValidationServiceCollectionExtensions.g.cs");
        Assert.Contains("AddZValidationAutoValidation", ext, StringComparison.Ordinal);
    }

    private static string RunAspNetGeneratorGetSource(string source, string fileName)
    {
        var sources = RunAspNetGeneratorGetSources(source);
        return sources.First(s => s.Contains(fileName.Replace(".g.cs", string.Empty), StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> RunAspNetGeneratorGetSources(string source)
    {
        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
    }
}
