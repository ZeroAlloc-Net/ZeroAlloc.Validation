using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// ZV0030: the generated validator calls a <c>[Must]</c>, <c>When</c>, <c>Unless</c> or
/// <c>[SkipWhen]</c> method as <c>instance.Name(...)</c>. A call that does not compile used to be
/// emitted anyway and surfaced as CS1061, CS1501, CS1503, CS0121, CS0411 and the like inside
/// generated code. Each call is now compiled first, in the generated file's context; one that
/// fails is skipped and reported at the attribute with the compiler's error, and the rest of the
/// model is still validated. Anything the compiler binds keeps working.
/// </summary>
public class MethodResolutionDiagnosticTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using ZeroAlloc.Validation;
        namespace TestModels;

        public readonly struct Truthy
        {
            private readonly bool _value;
            public Truthy(bool value) => _value = value;
            public static implicit operator bool(Truthy truthy) => truthy._value;
        }

        public static class Probe
        {
            public static string[] FailedProperties()
            {
                var result = new RequestValidator().Validate(new Request());
                var names = new string[result.Failures.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = result.Failures[i].PropertyName;
                return names;
            }
        }

        """;

    private const string FailingCheck =
        "IEnumerable<ValidationFailure> Check() { yield return new ValidationFailure { PropertyName = \"Check\", ErrorMessage = \"x\" }; }";

    [Theory]
    // [Must]: the wrong arity, the wrong parameter type and the wrong return type. A missing
    // name is not reported; see Member_missing_from_the_input_is_left_to_the_final_compilation.
    [InlineData("[Must(nameof(Ok))]", "public bool Ok() => false;", "Ok", "[Must] on 'Code'", "instance.Ok(instance.Code)", "CS1501")]
    [InlineData("[Must(nameof(Ok))]", "public bool Ok(string? a, string? b) => false;", "Ok", "[Must] on 'Code'", "instance.Ok(instance.Code)", "CS7036")]
    [InlineData("[Must(nameof(Ok))]", "public bool Ok(int value) => false;", "Ok", "[Must] on 'Code'", "instance.Ok(instance.Code)", "CS1503")]
    [InlineData("[Must(nameof(Ok))]", "public int Ok(string? value) => 0;", "Ok", "[Must] on 'Code'", "instance.Ok(instance.Code)", "CS0023")]
    [InlineData("[Must(nameof(Ok))]", "public bool? Ok(string? value) => false;", "Ok", "[Must] on 'Code'", "instance.Ok(instance.Code)", "CS0266")]
    // Overload resolution that fails.
    [InlineData("[Must(nameof(Ok))]", "public bool Ok(IComparable? value) => false; public bool Ok(IConvertible? value) => false;", "Ok", "[Must] on 'Code'", "instance.Ok(instance.Code)", "CS0121")]
    [InlineData("[Must(nameof(Ok))]", "public bool Ok<T>(List<T> value) => false;", "Ok", "[Must] on 'Code'", "instance.Ok(instance.Code)", "CS0411")]
    [InlineData("[Must(nameof(Ok))]", "public bool Ok<T>(T value) where T : struct => false;", "Ok", "[Must] on 'Code'", "instance.Ok(instance.Code)", "CS0453")]
    [InlineData("[Must(nameof(Ok))]", "public bool Ok(params int[] values) => false;", "Ok", "[Must] on 'Code'", "instance.Ok(instance.Code)", "CS1503")]
    // When and Unless.
    [InlineData("[NotEmpty(When = nameof(Ok))]", "public bool Ok(int value) => true;", "Ok", "When of [NotEmpty] on 'Code'", "instance.Ok()", "CS7036")]
    [InlineData("[NotEmpty(When = nameof(Ok))]", "public string Ok() => \"\";", "Ok", "When of [NotEmpty] on 'Code'", "instance.Ok()", "CS0019")]
    [InlineData("[NotEmpty(When = nameof(Ok))]", "public bool Ok<T>() => true;", "Ok", "When of [NotEmpty] on 'Code'", "instance.Ok()", "CS0411")]
    [InlineData("[NotEmpty(When = nameof(Ok))]", "public bool Ok(int x = 0) => true; public bool Ok(string s = \"\") => true;", "Ok", "When of [NotEmpty] on 'Code'", "instance.Ok()", "CS0121")]
    [InlineData("[NotEmpty(Unless = nameof(Ok))]", "public int Ok() => 0;", "Ok", "Unless of [NotEmpty] on 'Code'", "instance.Ok()", "CS0023")]
    public void Call_that_does_not_compile_reports_ZV0030_and_validates_the_rest(
        string rule, string members, string methodName, string usage, string call, string errorId)
    {
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                {{rule}} public string? Code { get; set; }

                {{members}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0030 = SingleDiagnostic(result, "ZV0030");
        Assert.Equal(DiagnosticSeverity.Error, zv0030.Severity);
        Assert.Equal(rule.Substring(1, rule.Length - 2), SpanText(zv0030));
        // The message quotes the compiler's own error for the call the validator would contain.
        Assert.StartsWith(
            $"Method '{methodName}', used by {usage}, cannot be called by the generated validator: '{call}' fails with {errorId}: ",
            zv0030.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "ZV0017" or "ZV0028");
        // Emitting proves there is no error left in generated code.
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Must_with_an_empty_name_reports_ZV0030()
    {
        var source = Prelude + """
            [Validate]
            public class Request
            {
                [Must("")] public string? Code { get; set; }

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0030 = SingleDiagnostic(result, "ZV0030");
        Assert.EndsWith("no method name is given", zv0030.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("[Validate]", "[Must(null!)] public string? Code { get; set; }", "Must(null!)")]
    [InlineData("[Validate][SkipWhen(null!)]", "", "SkipWhen(null!)")]
    public void Null_method_name_reports_ZV0030(string classAttributes, string member, string spanText)
    {
        var source = Prelude + $$"""
            {{classAttributes}}
            public class Request
            {
                {{member}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0030 = SingleDiagnostic(result, "ZV0030");
        Assert.Equal(spanText, SpanText(zv0030));
        Assert.EndsWith("no method name is given", zv0030.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("[Must(\"Missing\")] public string? Code { get; set; }", "")]
    [InlineData("[NotEmpty(When = \"Missing\")] public string? Code { get; set; }", "")]
    [InlineData("[NotEmpty(Unless = \"Missing\")] public string? Code { get; set; }", "")]
    [InlineData("[NotEmpty] public string? Code { get; set; }", "[SkipWhen(\"Missing\")]")]
    public void Member_missing_from_the_input_is_left_to_the_final_compilation(string member, string classAttributes)
    {
        // Another source generator may add the member, and only the final compilation contains
        // it. So the call is emitted as it was before 2.0, not reported, and when nothing adds
        // the member the final compilation fails with CS1061, as it always did.
        var source = Prelude + $$"""
            [Validate]
            {{classAttributes}}
            public class Request
            {
                {{member}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.True(HasMemberNotFound(output));
    }

    [Theory]
    [InlineData("[Must(\"IsOk\")]")]
    [InlineData("[Must(\"IsOkExtension\")]")]
    [InlineData("[NotEmpty(When = \"IsOn\")]")]
    public void Member_added_by_another_generator_is_called(string rule)
    {
        // The probe cannot see what another generator adds: the method or the extension method
        // below exists only in the final compilation. The call is emitted, compiles and runs.
        var source = Prelude + $$"""
            [Validate]
            public partial class Request
            {
                {{rule}} public string? Code { get; set; }

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source, new AddsMembersGenerator());

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Other" }, FailedProperties(output));
    }

    /// <summary>A second generator that adds members the model's rules call.</summary>
    private sealed class AddsMembersGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterPostInitializationOutput(static ctx => ctx.AddSource("Added.g.cs", """
                namespace TestModels;

                public partial class Request
                {
                    public bool IsOk(string? value) => false;
                    public bool IsOn() => true;
                }

                public static class RequestAddedExtensions
                {
                    public static bool IsOkExtension(this Request request, string? value) => false;
                }
                """));
    }

    [Fact]
    public void Extension_method_in_scope_of_the_generated_file_is_used()
    {
        // The generated file is in the model's namespace, so instance.Ok(value) binds to this
        // extension method, as it did in 1.x.
        var source = Prelude + """
            public static class RequestExtensions
            {
                public static bool Ok(this Request request, string? value) => false;
            }

            [Validate]
            public class Request
            {
                [Must("Ok")] public string? Code { get; set; }

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Extension_method_imported_only_by_the_model_file_is_left_to_the_final_compilation()
    {
        // The model's file imports Helpers, the generated file does not, so the call fails with
        // CS1061 there. CS1061 can also mean another generator adds the member, so it is not
        // reported: the call is emitted as it was before 2.0, and the final compilation fails.
        var source = """
            using System;
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            using Helpers;

            namespace Helpers
            {
                public static class RequestExtensions
                {
                    public static bool Ok(this TestModels.Request request, string? value) => false;
                }
            }

            namespace TestModels
            {
                public static class Probe
                {
                    public static string[] FailedProperties()
                    {
                        var result = new RequestValidator().Validate(new Request());
                        var names = new string[result.Failures.Length];
                        for (int i = 0; i < names.Length; i++)
                            names[i] = result.Failures[i].PropertyName;
                        return names;
                    }
                }

                [Validate]
                public class Request
                {
                    [Must("Ok")] public string? Code { get; set; }

                    [NotEmpty] public string? Other { get; set; }
                }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.True(HasMemberNotFound(output));
    }

    [Fact]
    public void Static_candidate_still_reports_ZV0028_not_ZV0030()
    {
        // The call binds to a static method, which is ZV0028's case even though it returns int.
        var source = Prelude + """
            [Validate]
            public class Request
            {
                [Must(nameof(Ok))] public string? Code { get; set; }

                public static int Ok(string? value) => 0;

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleDiagnostic(result, "ZV0028");
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0030", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("public bool Ok<T>(T value) => false;")]
    [InlineData("public bool Ok<T>(IEnumerable<T> value) => false;")]
    [InlineData("public bool Ok(params string?[] values) => false;")]
    [InlineData("public bool Ok(string? value, int limit = 0) => false;")]
    [InlineData("public bool Ok(object? value) => false;")]
    [InlineData("public bool Ok(in string? value) => false;")]
    [InlineData("public bool Ok(string? value) => false; public bool Ok<T>(T value) => true;")]
    [InlineData("public bool Ok(object? value) => true; public bool Ok(string? value) => false;")]
    // With an instance receiver, a static overload is removed before the best one is picked.
    [InlineData("public static bool Ok(string? value) => true; public bool Ok(object? value) => false;")]
    // Anything else the compiler binds works too, as it did in 1.x.
    [InlineData("public Func<string?, bool> Ok { get; } = _ => false;")]
    [InlineData("public Truthy Ok(string? value) => new Truthy(false);")]
    [InlineData("public bool Ok(ref readonly string? value) => false;")]
    public void Must_that_resolves_is_used(string members)
    {
        // Control: each of these binds to an instance method returning false, so Code fails.
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                [Must(nameof(Ok))] public string? Code { get; set; }

                {{members}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("public bool Ok(int x = 0) => true;")]
    [InlineData("public bool Ok(params int[] values) => true;")]
    [InlineData("public bool Ok() => true; public bool Ok(int x = 0) => false;")]
    [InlineData("internal bool Ok() => true;")]
    [InlineData("public Func<bool> Ok => () => true;")]
    [InlineData("public Truthy Ok() => new Truthy(true);")]
    public void When_that_resolves_is_used(string members)
    {
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                [NotEmpty(When = nameof(Ok))] public string? Code { get; set; }

                {{members}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Other" }, FailedProperties(output));
    }

    [Theory]
    // The model's Ok(int) cannot take a string, so the lookup goes on to the base type.
    [InlineData("public bool Ok(int value) => true;")]
    // The model's Ok(object?) could, but the validator cannot access it.
    [InlineData("private bool Ok(object? value) => true;")]
    // A property is not invocable, so it does not hide the base method.
    [InlineData("public new bool Ok => true;")]
    public void Inapplicable_or_inaccessible_overload_on_the_model_does_not_hide_the_base_method(string modelMember)
    {
        // Issue #233's lookup question: a more-derived type declaring an Ok that the call cannot
        // use leaves the base type's Ok(string?) as the method the call binds to.
        var source = Prelude + $$"""
            public class RequestBase
            {
                public bool Ok(string? value) => false;
            }

            [Validate]
            public class Request : RequestBase
            {
                [Must("Ok")] public string? Code { get; set; }

                {{modelMember}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Applicable_overload_on_the_model_hides_a_better_base_overload()
    {
        // C# removes base candidates once a more-derived type has an applicable one, even when the
        // base overload is the better match. Ok(object?) returns true, so Code passes.
        var source = Prelude + """
            public class RequestBase
            {
                public bool Ok(string? value) => false;
            }

            [Validate]
            public class Request : RequestBase
            {
                [Must(nameof(Ok))] public string? Code { get; set; }

                public bool Ok(object? value) => true;

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Override_is_looked_up_as_the_method_it_overrides()
    {
        // The override counts as RequestBase's Ok(object?), so Ok(string?) on the same level is
        // the better candidate. It returns false, so Code fails.
        var source = Prelude + """
            public class RequestBase
            {
                public virtual bool Ok(object? value) => true;
                public bool Ok(string? value) => false;
            }

            [Validate]
            public class Request : RequestBase
            {
                [Must(nameof(Ok))] public string? Code { get; set; }

                public override bool Ok(object? value) => true;

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Code", "Other" }, FailedProperties(output));
    }

    [Theory]
    // [Must] cases: a string? argument.
    [InlineData("public bool Ok(string? value) => false;", "public bool Ok(int value) => true;", true)]
    [InlineData("public bool Ok(string? value) => false;", "private bool Ok(object? value) => true;", true)]
    [InlineData("public bool Ok(string? value) => false;", "public static bool Ok(object? value) => true;", true)]
    [InlineData("public bool Ok(string? value) => false;", "public new bool Ok => true;", true)]
    [InlineData("public virtual bool Ok(object? value) => true; public bool Ok(string? value) => false;", "public override bool Ok(object? value) => true;", true)]
    [InlineData("", "public static bool Ok(string? value) => true; public bool Ok(object? value) => false;", true)]
    [InlineData("", "public bool Ok(IComparable? value) => false; public bool Ok(IConvertible? value) => false;", true)]
    [InlineData("", "public bool Ok<T>(T value) => false;", true)]
    [InlineData("", "public bool Ok<T>(T value) where T : struct => false;", true)]
    [InlineData("", "public bool Ok<T>(T value) where T : class => false;", true)]
    [InlineData("", "public bool Ok<T>(T value) where T : IComparable => false;", true)]
    [InlineData("", "public bool Ok<T>(T value) where T : new() => false;", true)]
    [InlineData("", "public bool Ok<T>(List<T> value) => false;", true)]
    [InlineData("", "public bool Ok<T>(IEnumerable<T> value) => false;", true)]
    [InlineData("", "public bool Ok<T>(T? value) where T : struct => false;", true)]
    [InlineData("", "public bool Ok(params int[] values) => false;", true)]
    [InlineData("", "public bool Ok(params string?[] values) => false;", true)]
    [InlineData("", "public bool Ok(string? value, params string?[] rest) => false; public bool Ok(params string?[] values) => false;", true)]
    [InlineData("", "public bool Ok(string? value, int limit = 0) => false; public bool Ok(string? value) => false;", true)]
    [InlineData("", "public bool Ok(string? value) => false; public bool Ok(in string? value) => false;", true)]
    [InlineData("", "public bool Ok(string? value) => false; public static bool Ok<T>(T value) => false;", true)]
    [InlineData("", "public bool Ok(ref string? value) => false;", true)]
    [InlineData("", "public int Ok(string? value) => 0;", true)]
    [InlineData("", "public bool Ok() => false;", true)]
    [InlineData("", "", true)]
    [InlineData("", "", false)]
    // A pointer argument needs an unsafe context, which the generated validator does not have.
    [InlineData("", "public unsafe bool Ok(int* value) => false;", true, "unsafe int*")]
    // Shapes the fast path in front of the probe accepts, and near misses it must leave alone.
    [InlineData("", "public bool Ok(string? value) => false;", true)]
    [InlineData("public bool Ok(string? value) => false;", "", true)]
    [InlineData("", "public bool Ok(string value) => false;", true)]
    [InlineData("", "[Obsolete(\"x\", true)] public bool Ok(string? value) => false;", true)]
    [InlineData("", "public bool Ok(string? value) => false; public static bool Ok(object? value) => true;", true)]
    [InlineData("", "internal bool Ok() => true;", false)]
    [InlineData("", "[Obsolete(\"x\", true)] public bool Ok() => true;", false)]
    [InlineData("", "public Func<string?, bool> Ok { get; } = _ => false;", true)]
    [InlineData("public bool Ok(string? value) => false;", "public new Func<string?, bool> Ok { get; } = _ => false;", true)]
    [InlineData("", "public bool? Ok(string? value) => false;", true)]
    [InlineData("", "public bool Ok(ref readonly string? value) => false;", true)]
    // When cases: no argument.
    [InlineData("public bool Ok() => true;", "public bool Ok(int value) => true;", false)]
    [InlineData("public bool Ok() => true;", "public static bool Ok(int x = 0) => true;", false)]
    [InlineData("", "public bool Ok(int x = 0) => true; public bool Ok(string s = \"\") => true;", false)]
    [InlineData("", "public bool Ok() => true; public bool Ok(int x = 0) => true;", false)]
    [InlineData("", "public bool Ok(params int[] values) => true;", false)]
    [InlineData("", "public bool Ok(params int[] values) => true; public bool Ok(int x = 0) => true;", false)]
    [InlineData("", "public bool Ok<T>() => true;", false)]
    [InlineData("", "public bool Ok(int value) => true;", false)]
    public void Generator_resolves_the_call_as_the_compiler_does(string baseMembers, string modelMembers, bool isMust, string codeType = "string?")
    {
        // The generator's verdict is compared with the compiler's own on the same call, written
        // by hand in a top-level class of the model's namespace, where the validator lives.
        var model = ComparisonModel(baseMembers, modelMembers, isMust, codeType);
        var call = isMust ? "!instance.Ok(instance.Code)" : "instance.Ok() && true";
        var handWritten = $$"""
            namespace TestModels;

            internal static class HandWritten
            {
                internal static bool Call(global::TestModels.Request instance) => {{call}};
            }
            """;

        var compiler = CSharpCompilation.Create(
            "HandWritten_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(model), CSharpSyntaxTree.ParseText(handWritten)],
            TrustedPlatformReferences(),
            Options());
        bool compilerBinds = !HasError(compiler);
        bool memberMissing = HasMemberNotFound(compiler);

        var (result, output) = RunGenerator(model);
        bool generatorBinds = !result.Diagnostics.Any(d => d.Id is "ZV0017" or "ZV0028" or "ZV0030");

        if (memberMissing)
        {
            // Not reported, since another generator could add the member: the call is emitted and
            // the final compilation reports the compiler's own error, as it did before 2.0.
            Assert.True(generatorBinds && HasMemberNotFound(output));
            return;
        }

        Assert.Equal(compilerBinds, generatorBinds);

        // The fast path only ever accepts, so it must never accept a call the compiler rejects.
        var request = compiler.GetTypeByMetadataName("TestModels.Request")!;
        var argumentType = isMust ? ((IPropertySymbol)request.GetMembers("Code")[0]).Type : null;
        if (CertainCall.Condition(compiler, request, "Ok", argumentType) is not null)
            Assert.True(compilerBinds, "The fast path accepted a call the compiler rejects.");
        EmitSucceeds(output);
    }

    [Theory]
    [InlineData("public bool Ok(string? value) => false;", true, true)]
    [InlineData("public bool Ok() => true;", false, true)]
    [InlineData("public bool Ok(string value) => false;", true, false)]
    [InlineData("public bool Ok(string? value) => false; public bool Ok(int value) => false;", true, false)]
    [InlineData("public bool Ok(in string? value) => false;", true, false)]
    [InlineData("public bool Ok<T>(T value) => false;", true, false)]
    [InlineData("public bool Ok(string? value, int x = 0) => false;", true, false)]
    [InlineData("[Obsolete] public bool Ok(string? value) => false;", true, false)]
    [InlineData("public static bool Ok(string? value) => false;", true, false)]
    [InlineData("private bool Ok(string? value) => false;", true, false)]
    [InlineData("public unsafe bool Ok(int* value) => false;", true, false, "unsafe int*")]
    [InlineData("public unsafe bool Ok(delegate*<void> value) => false;", true, false, "unsafe delegate*<void>")]
    public void Fast_path_accepts_only_a_single_exactly_typed_bool_method(string members, bool isMust, bool accepted, string codeType = "string?")
    {
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                public {{codeType}} Code { get; set; }

                {{members}}
            }
            """;
        var compilation = CSharpCompilation.Create(
            "FastPath_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences(),
            Options());
        var request = compilation.GetTypeByMetadataName("TestModels.Request")!;
        var argumentType = isMust ? ((IPropertySymbol)request.GetMembers("Code")[0]).Type : null;

        Assert.Equal(accepted, CertainCall.Condition(compilation, request, "Ok", argumentType) is not null);
    }

    [Fact]
    public void Validate_base_usage_that_the_model_breaks_is_reported_by_the_model()
    {
        // RequestBase's own validator calls its Ok() fine. Request declares a static Ok(int x = 0),
        // which the same call from Request's validator binds to, so Request drops the inherited
        // rule and must say so rather than leave it to RequestBase, which has nothing to report.
        var source = Prelude + """
            [Validate]
            public class RequestBase
            {
                [NotEmpty(When = nameof(Ok))] public string? Code { get; set; }

                public bool Ok() => true;
            }

            [Validate]
            public class Request : RequestBase
            {
                public static bool Ok(int x = 0) => true;

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0028 = SingleDiagnostic(result, "ZV0028");
        Assert.EndsWith("is static", zv0028.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    // ---- [SkipWhen], issue #231 -------------------------------------------------------------

    [Theory]
    [InlineData("private bool ShouldSkip() => true;", "ZV0028",
        "Method 'ShouldSkip', used by [SkipWhen] on 'Request', cannot be called from the generated validator because it is not accessible from it")]
    [InlineData("protected bool ShouldSkip() => true;", "ZV0028",
        "Method 'ShouldSkip', used by [SkipWhen] on 'Request', cannot be called from the generated validator because it is not accessible from it")]
    [InlineData("public static bool ShouldSkip() => true;", "ZV0028",
        "Method 'ShouldSkip', used by [SkipWhen] on 'Request', cannot be called from the generated validator because it is static")]
    [InlineData("public bool ShouldSkip(int value) => true;", "ZV0030",
        "Method 'ShouldSkip', used by [SkipWhen] on 'Request', cannot be called by the generated validator: 'instance.ShouldSkip()' fails with CS7036: ")]
    [InlineData("public int ShouldSkip() => 1;", "ZV0030",
        "Method 'ShouldSkip', used by [SkipWhen] on 'Request', cannot be called by the generated validator: 'instance.ShouldSkip()' fails with CS0029: ")]
    public void Unusable_SkipWhen_method_is_reported_and_the_model_is_validated(string member, string id, string message)
    {
        // The skip check is left out, so validation runs: an unusable skip condition fails the
        // build and never lets an invalid model through as valid.
        var source = Prelude + $$"""
            [Validate]
            [SkipWhen("ShouldSkip")]
            public class Request
            {
                {{member}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var diagnostic = SingleDiagnostic(result, id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("SkipWhen(\"ShouldSkip\")", SpanText(diagnostic));
        Assert.StartsWith(message, diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(1, result.Diagnostics.Count(d => d.Id.StartsWith("ZV", StringComparison.Ordinal)));
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Fact]
    public void Inaccessible_base_SkipWhen_method_is_ZV0028_not_ZV0017()
    {
        // [SkipWhen] is read from the model only, so the usage is the model's to change, and what
        // is dropped is the skip rather than a rule: an error, not ZV0017's warning.
        var source = Prelude + """
            public class RequestBase
            {
                protected bool ShouldSkip() => true;
            }

            [Validate]
            [SkipWhen("ShouldSkip")]
            public class Request : RequestBase
            {
                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        var zv0028 = SingleDiagnostic(result, "ZV0028");
        Assert.Equal("SkipWhen(\"ShouldSkip\")", SpanText(zv0028));
        Assert.Equal(
            "Method 'ShouldSkip', used by [SkipWhen] on 'Request', cannot be called from the generated validator because it is not accessible from it",
            zv0028.GetMessage(CultureInfo.InvariantCulture));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "ZV0017" or "ZV0030");
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("public bool ShouldSkip() => true;")]
    [InlineData("internal bool ShouldSkip() => true;")]
    [InlineData("public bool ShouldSkip(int x = 0) => true;")]
    public void Usable_SkipWhen_method_skips_validation(string member)
    {
        var source = Prelude + $$"""
            [Validate]
            [SkipWhen("ShouldSkip")]
            public class Request
            {
                {{member}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Empty(FailedProperties(output));
    }

    // ---- [CustomValidation] -----------------------------------------------------------------

    [Fact]
    public void Generic_CustomValidation_method_reports_ZV0013_only()
    {
        // instance.Check() cannot infer T, which is a signature problem: ZV0013, and nothing else.
        var source = Prelude + """
            [Validate]
            public class Request
            {
                [CustomValidation] public IEnumerable<ValidationFailure> Check<T>() { yield break; }

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleDiagnostic(result, "ZV0013");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "ZV0028" or "ZV0030");
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("[CustomValidation] public IEnumerable<ValidationFailure> Check(int x) { yield break; }")]
    [InlineData("[CustomValidation] public bool Check() => true;")]
    public void Invalid_CustomValidation_signature_is_not_also_ZV0030(string member)
    {
        var source = Prelude + $$"""
            [Validate]
            public class Request
            {
                {{member}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        SingleDiagnostic(result, "ZV0013");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "ZV0028" or "ZV0030");
        Assert.Equal(new[] { "Other" }, FailedProperties(output));
    }

    [Theory]
    [InlineData("public int Check(int x = 0) => 0;")]
    [InlineData("public static bool Check(int x = 0) => true;")]
    public void Base_CustomValidation_method_hidden_from_the_model_is_still_called(string modelMember)
    {
        // instance.Check() from the model would bind to the model's own Check, so the validator
        // calls the [CustomValidation] method through its declaring type instead.
        var source = Prelude + $$"""
            public class RequestBase
            {
                [CustomValidation] public {{FailingCheck}}
            }

            [Validate]
            public class Request : RequestBase
            {
                {{modelMember}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other", "Check" }, FailedProperties(output));
    }

    [Fact]
    public void Overridden_CustomValidation_method_is_called_virtually()
    {
        // The model's [CustomValidation] override sits beside a Check(int x = 0) that would win
        // instance.Check(), since lookup counts the override as RequestBase's method. The call
        // goes through RequestBase, which declares the virtual method, so the override runs.
        var source = Prelude + """
            public class RequestBase
            {
                public virtual IEnumerable<ValidationFailure> Check() { yield break; }
            }

            [Validate]
            public class Request : RequestBase
            {
                [CustomValidation] public override IEnumerable<ValidationFailure> Check()
                {
                    yield return new ValidationFailure { PropertyName = "Check", ErrorMessage = "x" };
                }

                public int Check(int x = 0) => 0;

                [NotEmpty] public string? Other { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("ZV", StringComparison.Ordinal));
        Assert.Equal(new[] { "Other", "Check" }, FailedProperties(output));
    }

    private static Diagnostic SingleDiagnostic(GeneratorDriverRunResult result, string id)
    {
        var matches = result.Diagnostics
            .Where(d => string.Equals(d.Id, id, StringComparison.Ordinal))
            .ToArray();
        Assert.True(matches.Length == 1, $"Expected exactly one {id}, got: "
            + string.Join("; ", result.Diagnostics.Select(d => d.ToString())));
        return matches[0];
    }

    private static string ComparisonModel(string baseMembers, string modelMembers, bool isMust, string codeType) => $$"""
            using System;
            using System.Collections.Generic;
            using ZeroAlloc.Validation;
            namespace TestModels;

            public class RequestBase
            {
                {{baseMembers}}
            }

            [Validate]
            public class Request : RequestBase
            {
                {{(isMust ? "[Must(\"Ok\")]" : "[NotEmpty(When = \"Ok\")]")}} public {{codeType}} Code { get; set; }

                {{modelMembers}}

                [NotEmpty] public string? Other { get; set; }
            }
            """;

    private static bool HasMemberNotFound(Compilation compilation)
    {
        foreach (var diagnostic in compilation.GetDiagnostics())
        {
            if (diagnostic.Id is "CS1061" or "CS0117" or "CS1929") return true;
        }
        return false;
    }

    private static bool HasError(Compilation compilation)
    {
        foreach (var diagnostic in compilation.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error) return true;
        }
        return false;
    }

    private static string SpanText(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static byte[] EmitSucceeds(Compilation output)
    {
        using var peStream = new MemoryStream();
        var emit = output.Emit(peStream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())));
        return peStream.ToArray();
    }

    /// <summary>
    /// Emits the generator's output compilation, which proves the generated validator compiles,
    /// then runs it against a default <c>Request</c> and returns the property names that failed.
    /// </summary>
    private static string[] FailedProperties(Compilation output)
    {
        var assembly = System.Reflection.Assembly.Load(EmitSucceeds(output));
        var probe = assembly.GetType("TestModels.Probe", throwOnError: true)!;
        return (string[])probe.GetMethod("FailedProperties")!.Invoke(null, null)!;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

    private static CSharpCompilationOptions Options() =>
        new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true);

    private static (GeneratorDriverRunResult Result, Compilation Output) RunGenerator(string source, IIncrementalGenerator? other = null)
    {
        var compilation = CSharpCompilation.Create(
            "MethodResolutionTests_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences(),
            Options());

        var generators = other is null
            ? new[] { new ValidatorGenerator().AsSourceGenerator() }
            : new[] { other.AsSourceGenerator(), new ValidatorGenerator().AsSourceGenerator() };
        var driver = CSharpGeneratorDriver.Create(generators)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }
}
