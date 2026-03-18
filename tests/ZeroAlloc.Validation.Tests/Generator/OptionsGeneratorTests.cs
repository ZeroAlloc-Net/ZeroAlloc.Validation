using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Generator;

public class OptionsGeneratorTests
{
    [Fact]
    public void Generator_EmitsValidateWithZeroAlloc_ForValidateClass()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("ValidateWithZeroAlloc",                             generated, System.StringComparison.Ordinal);
        Assert.Contains("OptionsBuilder<global::MyApp.DatabaseOptions>",     generated, System.StringComparison.Ordinal);
        Assert.Contains("IValidateOptions<global::MyApp.DatabaseOptions>",   generated, System.StringComparison.Ordinal);
        Assert.Contains("ZeroAllocOptionsValidator<global::MyApp.DatabaseOptions>", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsValidatorFor_TryAddSingleton()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class SmtpOptions { [NotEmpty] public string Host { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::MyApp.SmtpOptions>", generated, System.StringComparison.Ordinal);
        Assert.Contains("global::MyApp.SmtpOptionsValidator",                                                   generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsTwoOverloads_ForTwoValidateClasses()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            [Validate] public class SmtpOptions     { [NotEmpty] public string Host             { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.Contains("OptionsBuilder<global::MyApp.DatabaseOptions>", generated, System.StringComparison.Ordinal);
        Assert.Contains("OptionsBuilder<global::MyApp.SmtpOptions>",     generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NonValidateType_NoOverloadEmitted()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace MyApp;
            [Validate] public class DatabaseOptions { [NotEmpty] public string ConnectionString { get; set; } = ""; }
            public class NotOptions { public string X { get; set; } = ""; }
            """;

        var generated = RunOptionsGenerator(source);

        Assert.DoesNotContain("NotOptions", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NoValidateClasses_EmitsNothing()
    {
        var source = """
            namespace MyApp;
            public class Plain { public string X { get; set; } = ""; }
            """;

        var trees = RunOptionsGeneratorAllTrees(source);
        Assert.Empty(trees);
    }

    private static string RunOptionsGenerator(string source)
        => RunOptionsGeneratorAllTrees(source).First();

    private static System.Collections.Generic.IReadOnlyList<string> RunOptionsGeneratorAllTrees(string source)
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

        var generator = new ZeroAlloc.Validation.Options.Generator.OptionsValidationEmitter();
        var driver    = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
    }
}
