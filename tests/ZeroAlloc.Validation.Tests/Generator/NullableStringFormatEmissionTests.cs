using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// [EmailAddress] and [Matches] check a string only when one is present, so a null passes and an
/// empty string is still checked. See #280.
/// </summary>
public class NullableStringFormatEmissionTests
{
    [Theory]
    [InlineData("string?")]
    [InlineData("string")]
    public void EmailAndMatches_AreGuardedAgainstNull_AndCompileCleanly(string type)
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;
            [Validate]
            public class Foo
            {
                [EmailAddress] public {{type}} Email { get; set; } = default!;
                [Matches(@"^\d+$")] public {{type}} Digits { get; set; } = default!;
            }
            """;
        var (result, output) = GeneratorTestHelper.RunGeneratorAndUpdateCompilation(
            source, nullableContextOptions: NullableContextOptions.Enable);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TestModels.FooValidator.g.cs");

        Assert.Contains(
            "instance.Email is not null && (!global::ZeroAlloc.Validation.Internal.EmailValidator.IsValid(instance.Email))",
            generated, StringComparison.Ordinal);
        Assert.Contains(
            "instance.Digits is not null && (!__Regex_Digits.IsMatch(instance.Digits))",
            generated, StringComparison.Ordinal);

        var generatedTrees = output.SyntaxTrees.Skip(1).ToHashSet();
        var problems = output.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning
                        && d.Location.SourceTree is { } tree && generatedTrees.Contains(tree))
            .Select(d => d.ToString())
            .ToArray();
        Assert.Empty(problems);
    }
}
