using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Generator;

namespace ZeroAlloc.Validation.Tests.Generator;

public class CustomRuleAttributeTests
{
    private const string NotBlankDeclaration = """
        public sealed class NotBlankAttribute : ValidationAttribute<string?>
        {
            public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
        }
        """;

    [Fact]
    public void NotBlank_without_arguments_emits_static_field_and_IsValid_call()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed class Request
            {
                [NotBlank] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("private static readonly global::TestModels.NotBlankAttribute __Rule_Name_0", src, StringComparison.Ordinal);
        Assert.Contains("= new global::TestModels.NotBlankAttribute();", src, StringComparison.Ordinal);
        Assert.Contains("!__Rule_Name_0.IsValid(instance.Name)", src, StringComparison.Ordinal);
        Assert.Contains("ErrorMessage = \"Name is invalid.\"", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_and_named_arguments_of_every_constant_kind_are_rebuilt()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public enum Color { Red = 1, Green = 2 }

            public sealed class AllKindsAttribute : ValidationAttribute<string?>
            {
                public AllKindsAttribute(int i, string s, char c, Color e, System.Type t, int[] a, string? n) { }
                public bool Flag { get; set; }
                public override bool IsValid(string? value) => value is not null;
            }

            [Validate]
            public sealed class Request
            {
                [AllKinds(3, "x\"y", 'q', Color.Red, typeof(System.Guid), new[] { 1, 2 }, null, Flag = true, Message = "m", ErrorCode = "E")]
                public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        var initializer = FieldInitializer(src, "__Rule_Name_0");
        Assert.Contains("new global::TestModels.AllKindsAttribute(3, ", initializer, StringComparison.Ordinal);
        Assert.Contains("\"x\\\"y\"", initializer, StringComparison.Ordinal);
        Assert.Contains("'q'", initializer, StringComparison.Ordinal);
        Assert.Contains("(global::TestModels.Color)1", initializer, StringComparison.Ordinal);
        Assert.Contains("typeof(global::System.Guid)", initializer, StringComparison.Ordinal);
        Assert.Contains("new int[] { 1, 2 }", initializer, StringComparison.Ordinal);
        Assert.Contains(", null)", initializer, StringComparison.Ordinal);
        Assert.Contains("{ Flag = true }", initializer, StringComparison.Ordinal);
        Assert.DoesNotContain("Message =", initializer, StringComparison.Ordinal);
        Assert.DoesNotContain("ErrorCode =", initializer, StringComparison.Ordinal);
    }

    [Fact]
    public void Numeric_negative_enum_and_open_generic_arguments_compile()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public enum Signed : long { Minus = -1 }

            public sealed class NumbersAttribute : ValidationAttribute<string?>
            {
                public NumbersAttribute(float f, double d, long l, uint u, ulong ul, byte b, short s, Signed e, System.Type open, object boxed, string[] empty) { }
                public double NamedDouble { get; set; }
                public override bool IsValid(string? value) => value is not null;
            }

            [Validate]
            public sealed class Request
            {
                [Numbers(1.5f, 2.25, 9000000000L, 4000000000u, 18000000000000000000ul, 255, -3, Signed.Minus, typeof(System.Collections.Generic.Dictionary<,>), 7, new string[0], NamedDouble = 0.1)]
                public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Equal(
            "= new global::TestModels.NumbersAttribute(1.5F, 2.25D, 9000000000L, 4000000000U, 18000000000000000000UL, 255, -3, "
                + "(global::TestModels.Signed)(-1L), typeof(global::System.Collections.Generic.Dictionary<,>), 7, new string[] {}) { NamedDouble = 0.1D };",
            FieldInitializer(src, "__Rule_Name_0"));
    }

    [Fact]
    public void Indirect_base_is_recognised()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public abstract class StringRule : ValidationAttribute<string?> { }

            public sealed class NoDigits : StringRule
            {
                public override bool IsValid(string? value) => value is null || !System.Linq.Enumerable.Any(value, char.IsDigit);
            }

            [Validate]
            public sealed class Request
            {
                [NoDigits] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("private static readonly global::TestModels.NoDigits __Rule_Name_0", src, StringComparison.Ordinal);
        Assert.Contains("!__Rule_Name_0.IsValid(instance.Name)", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Closed_generic_attribute_uses_closed_type_name()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class InRange<T>(T min, T max) : ValidationAttribute<T> where T : System.IComparable<T>
            {
                public override bool IsValid(T value) => value.CompareTo(min) >= 0 && value.CompareTo(max) <= 0;
            }

            [Validate]
            public sealed class Request
            {
                [InRange<int>(1, 5)] public int Count { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("private static readonly global::TestModels.InRange<int> __Rule_Count_0", src, StringComparison.Ordinal);
        Assert.Contains("= new global::TestModels.InRange<int>(1, 5);", src, StringComparison.Ordinal);
        Assert.Contains("!__Rule_Count_0.IsValid(instance.Count)", src, StringComparison.Ordinal);
    }

    [Fact]
    public void When_Unless_Severity_ErrorCode_and_Message_apply_to_custom_rules()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            [Validate]
            public sealed class Request
            {
                public bool Active { get; set; }

                [NotBlank(When = nameof(Cond), Unless = nameof(Skip), Message = "{PropertyName} needs text", ErrorCode = "BLANK", Severity = Severity.Warning)]
                public string? Name { get; init; }

                public bool Cond() => Active;
                public bool Skip() => !Active;
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("instance.Cond() && !instance.Skip() && !__Rule_Name_0.IsValid(instance.Name)", src, StringComparison.Ordinal);
        Assert.Contains("ErrorMessage = \"Name needs text\"", src, StringComparison.Ordinal);
        Assert.Contains("ErrorCode = \"BLANK\"", src, StringComparison.Ordinal);
        Assert.Contains("Severity = global::ZeroAlloc.Validation.Severity.Warning", src, StringComparison.Ordinal);
        Assert.Equal("= new global::TestModels.NotBlankAttribute();", FieldInitializer(src, "__Rule_Name_0"));
    }

    [Fact]
    public void StopOnFirstFailure_chains_custom_rule_with_else_if()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            [Validate]
            public sealed class Request
            {
                [StopOnFirstFailure]
                [NotEmpty]
                [NotBlank]
                public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("else if (!__Rule_Name_1.IsValid", src, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueObject_property_passes_wrapper()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int value) => Value = value;
            }

            public sealed class KnownCustomerAttribute : ValidationAttribute<CustomerId>
            {
                public override bool IsValid(CustomerId value) => value.Value > 0;
            }

            [Validate]
            public partial class PlaceOrderCommand
            {
                [KnownCustomer]
                public CustomerId CustomerId { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "PlaceOrderCommandValidator.g.cs");
        Assert.Contains("IsValid(instance.CustomerId)", src, StringComparison.Ordinal);
        Assert.DoesNotContain("instance.CustomerId.Value", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreachable_When_on_custom_rule_reports_ZV0017()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            public class AuditBase
            {
                [NotBlank(When = nameof(IsAudited))]
                public string? ModifiedBy { get; init; }

                private bool IsAudited() => true;
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
        var src = GetGeneratedSource(result, "AuditedValidator.g.cs");
        Assert.DoesNotContain("__Rule_", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_and_async_paths_emit_field_once()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            using ZeroAlloc.Pipeline;
            using System.Threading;
            using System.Threading.Tasks;
            namespace TestModels;

            {{NotBlankDeclaration}}

            [Validate]
            public class Request
            {
                [NotBlank] public string? Name { get; set; }
            }

            [PipelineBehavior(Order = 0, AppliesTo = typeof(Request))]
            public class AuditBehavior : IPipelineBehavior
            {
                public static async ValueTask<ZeroAlloc.Validation.ValidationResult> Handle<TModel>(
                    TModel instance,
                    CancellationToken ct,
                    System.Func<TModel, CancellationToken, ValueTask<ZeroAlloc.Validation.ValidationResult>> next)
                    => await next(instance, ct);
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ValidateAsync", src, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(src, "private static readonly global::TestModels.NotBlankAttribute"));
        Assert.Equal(2, CountOccurrences(src, "!__Rule_Name_0.IsValid("));
    }

    [Fact]
    public void Builtin_output_unchanged()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed class Request
            {
                [NotEmpty] public string? Name { get; init; }
                [Matches("a+")] public string? Code { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.DoesNotContain("__Rule_", src, StringComparison.Ordinal);
        Assert.Contains("private static readonly global::System.Text.RegularExpressions.Regex __Regex_Code", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Protected_base_property_with_only_custom_rule_reports_ZV0017()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            public class AuditBase
            {
                [NotBlank]
                protected string? ModifiedBy { get; init; }
            }

            [Validate]
            public class Audited : AuditBase
            {
                [GreaterThan(0)]
                public int Total { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZV0017", StringComparison.Ordinal));
        Assert.DoesNotContain("ModifiedBy", GetGeneratedSource(result, "AuditedValidator.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Identical_custom_rule_twice_reports_ZV0018()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            [Validate]
            public sealed class Request
            {
                [NotBlank]
                [NotBlank]
                public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(1, CountDiagnostics(result, "ZV0018"));
    }

    [Fact]
    public void Same_custom_rule_with_different_arguments_does_not_report_ZV0018()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class MinWordsAttribute(int minWords) : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => (value?.Split(' ').Length ?? 0) >= minWords;
            }

            [Validate]
            public sealed class Request
            {
                [MinWords(2)]
                [MinWords(3)]
                public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0018", StringComparison.Ordinal));
    }

    [Fact]
    public void Custom_rule_on_multi_property_value_object_does_not_report_ZV0016()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct Money
            {
                public decimal Amount { get; }
                public string Currency { get; }
                public Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = currency;
                }
            }

            public sealed class PositiveMoneyAttribute : ValidationAttribute<Money>
            {
                public override bool IsValid(Money value) => value.Amount > 0;
            }

            [Validate]
            public partial class PriceCommand
            {
                [PositiveMoney]
                public Money Total { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0016", StringComparison.Ordinal));
        Assert.Contains("!__Rule_Total_0.IsValid(instance.Total)", GetGeneratedSource(result, "PriceCommandValidator.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_return_path_emits_custom_rule_with_all_base_members()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            [Validate(StopOnFirstFailure = true)]
            public sealed class Request
            {
                public bool Active { get; set; }

                [NotBlank(When = nameof(Cond), Message = "{PropertyName} needs text", ErrorCode = "BLANK", Severity = Severity.Warning)]
                public string? Name { get; init; }

                public bool Cond() => Active;
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = NormalizeNewLines(GetGeneratedSource(result, "RequestValidator.g.cs"));
        Assert.DoesNotContain("_buf", src, StringComparison.Ordinal);
        Assert.Contains(
            "        if (instance.Cond() && !__Rule_Name_0.IsValid(instance.Name))\n"
                + "        {\n"
                + "            return new global::ZeroAlloc.Validation.ValidationResult(new global::ZeroAlloc.Validation.ValidationFailure[]\n"
                + "            {\n"
                + "                new global::ZeroAlloc.Validation.ValidationFailure { PropertyName = \"Name\", ErrorMessage = \"Name needs text\", "
                + "ErrorCode = \"BLANK\", Severity = global::ZeroAlloc.Validation.Severity.Warning }\n",
            src,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(src, "private static readonly global::TestModels.NotBlankAttribute __Rule_Name_0"));
    }

    [Fact]
    public void Nested_validator_path_emits_custom_rule_with_all_base_members()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            [Validate]
            public class Address
            {
                [NotBlank] public string? Street { get; set; }
            }

            [Validate]
            public class Customer
            {
                public bool Active { get; set; }

                [NotBlank(When = nameof(Cond), Message = "{PropertyName} needs text", ErrorCode = "BLANK", Severity = Severity.Warning)]
                public string? Name { get; set; }

                public Address Home { get; set; } = new();

                public bool Cond() => Active;
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = NormalizeNewLines(GetGeneratedSource(result, "CustomerValidator.g.cs"));
        Assert.Contains("global::TestModels.AddressValidator", src, StringComparison.Ordinal);
        Assert.Contains(
            "        if (instance.Cond() && !__Rule_Name_0.IsValid(instance.Name))\n"
                + "            _buf.Add(new global::ZeroAlloc.Validation.ValidationFailure { PropertyName = \"Name\", ErrorMessage = \"Name needs text\", "
                + "ErrorCode = \"BLANK\", Severity = global::ZeroAlloc.Validation.Severity.Warning });\n",
            src,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(src, "private static readonly global::TestModels.NotBlankAttribute __Rule_Name_0"));
        Assert.Contains("!__Rule_Street_0.IsValid(instance.Street)", GetGeneratedSource(result, "AddressValidator.g.cs"), StringComparison.Ordinal);
    }

    private const string MinWordsDeclaration = """
        [RuleMessage("{PropertyName} needs {minWords} words, {MinWords} min.")]
        public sealed class MinWordsAttribute(int minWords) : ValidationAttribute<string?>
        {
            public int MinWords { get; } = minWords;
            public override bool IsValid(string? value) =>
                value is null || value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length >= MinWords;
        }
        """;

    [Fact]
    public void RuleMessage_supplies_default_message_and_error_code()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} must not be blank.", ErrorCode = "NOT_BLANK")]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed class Request
            {
                [NotBlank] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ErrorMessage = \"Name must not be blank.\", ErrorCode = \"NOT_BLANK\"", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_Message_and_ErrorCode_override_RuleMessage()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} must not be blank.", ErrorCode = "NOT_BLANK")]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed class Request
            {
                [NotBlank(Message = "custom", ErrorCode = "X")] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ErrorMessage = \"custom\", ErrorCode = \"X\"", src, StringComparison.Ordinal);
        Assert.DoesNotContain("must not be blank", src, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT_BLANK", src, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleMessage_is_found_on_base_class()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} base msg.")]
            public abstract class StringRule : ValidationAttribute<string?> { }

            public sealed class NotBlankAttribute : StringRule
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed class Request
            {
                [NotBlank] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ErrorMessage = \"Name base msg.\"", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Nearest_RuleMessage_wins_over_base_class()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} base msg.", ErrorCode = "BASE")]
            public abstract class StringRule : ValidationAttribute<string?> { }

            [RuleMessage("{PropertyName} derived msg.")]
            public sealed class NotBlankAttribute : StringRule
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed class Request
            {
                [NotBlank] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ErrorMessage = \"Name derived msg.\" }", src, StringComparison.Ordinal);
        Assert.DoesNotContain("BASE", src, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleMessage_on_class_that_is_not_a_rule_reports_ZV0026()
    {
        // No [Validate] model: the warning does not depend on anything being generated.
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} must not be blank.")]
            public sealed class NotARule { }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(1, CountDiagnostics(result, "ZV0026"));
        var zv0026 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0026", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Warning, zv0026.Severity);
        Assert.Equal("RuleMessage(\"{PropertyName} must not be blank.\")", SpanText(zv0026));
        Assert.Equal(
            "'NotARule' has [RuleMessage] but does not derive from ValidationAttribute<T>, so the message is never used",
            zv0026.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RuleMessage_on_non_generic_ValidationAttribute_subclass_reports_ZV0026()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} legacy.")]
            public sealed class LegacyAttribute : ValidationAttribute { }
            """;

        var (result, _) = RunGenerator(source);

        Assert.Equal(1, CountDiagnostics(result, "ZV0026"));
    }

    [Fact]
    public void RuleMessage_on_direct_rule_reports_no_ZV0026()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} must not be blank.")]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountDiagnostics(result, "ZV0026"));
    }

    [Fact]
    public void RuleMessage_on_indirect_rule_reports_no_ZV0026()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public abstract class StringRule : ValidationAttribute<string?> { }

            [RuleMessage("{PropertyName} must not be blank.")]
            public sealed class NotBlankAttribute : StringRule
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountDiagnostics(result, "ZV0026"));
    }

    [Fact]
    public void RuleMessage_on_abstract_rule_base_reports_no_ZV0026()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} base msg.")]
            public abstract class StringRule : ValidationAttribute<string?> { }

            [RuleMessage("{PropertyName} generic base msg.")]
            public abstract class Rule<T> : ValidationAttribute<T> { }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountDiagnostics(result, "ZV0026"));
    }

    [Fact]
    public void ZV0026_check_is_cached_across_an_unrelated_edit()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} must not be blank.")]
            public sealed class NotARule { }
            """;
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ValidatorGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace TestModels; public sealed class Unrelated { }"));
        var result = driver.RunGenerators(edited).GetRunResult();

        Assert.Equal(1, CountDiagnostics(result, "ZV0026"));
        var outputs = result.Results[0].TrackedSteps[ValidatorGenerator.MisplacedRuleMessageTrackingName]
            .SelectMany(s => s.Outputs)
            .ToList();
        Assert.NotEmpty(outputs);
        Assert.All(
            outputs,
            o => Assert.True(
                o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected a cached step, got {o.Reason}"));
    }

    [Fact]
    public void Named_placeholders_resolve_from_ctor_parameter_and_property()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{MinWordsDeclaration}}

            [Validate]
            public sealed class Post
            {
                [MinWords(3)] public string? Title { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "PostValidator.g.cs");
        Assert.Contains("ErrorMessage = \"Title needs 3 words, {MinWords} min.\"", src, StringComparison.Ordinal);
        Assert.Equal(1, CountDiagnostics(result, "ZV0022"));
        var zv0022 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0022", StringComparison.Ordinal));
        Assert.Equal(
            "Placeholder 'MinWords' in the message for 'MinWordsAttribute' on 'Title' does not match any argument; it is emitted literally",
            zv0022.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Named_placeholder_from_named_argument()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} {Tag}")]
            public sealed class TaggedAttribute : ValidationAttribute<string?>
            {
                public string? Tag { get; set; }
                public override bool IsValid(string? value) => value is not null;
            }

            [Validate]
            public sealed class Request
            {
                [Tagged(Tag = "abc")] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ErrorMessage = \"Name abc\"", src, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0022", StringComparison.Ordinal));
    }

    [Fact]
    public void Placeholders_in_usage_Message_resolve_too()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{MinWordsDeclaration}}

            [Validate]
            public sealed class Post
            {
                [MinWords(4, Message = "{PropertyName}: {minWords}+")] public string? Title { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "PostValidator.g.cs");
        Assert.Contains("ErrorMessage = \"Title: 4+\"", src, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0022", StringComparison.Ordinal));
    }

    [Fact]
    public void Placeholder_values_format_invariant_and_by_kind()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public enum Mode { Strict = 1 }

            [RuleMessage("{PropertyName} {ratio}|{mode}|{raw}|{type}|{items}|{text}|{none}|{ch}|{flag}")]
            public sealed class ShapeAttribute(double ratio, Mode mode, Mode raw, System.Type type, int[] items, string text, string? none, char ch, bool flag)
                : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => value is not null;
            }

            [Validate]
            public sealed class Request
            {
                [Shape(1.5, Mode.Strict, (Mode)7, typeof(System.Guid), new[] { 1, 2 }, "t", null, 'c', true)]
                public string? Name { get; init; }
            }
            """;

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
        try
        {
            var (result, output) = RunGenerator(source);

            Assert.Empty(CompileErrors(output));
            var src = GetGeneratedSource(result, "RequestValidator.g.cs");
            Assert.Contains("ErrorMessage = \"Name 1.5|Strict|7|System.Guid|1, 2|t||c|True\"", src, StringComparison.Ordinal);
            Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0022", StringComparison.Ordinal));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Unknown_placeholder_reports_ZV0022_warning_once()
    {
        var source = """
            using ZeroAlloc.Validation;
            using ZeroAlloc.Pipeline;
            using System.Threading;
            using System.Threading.Tasks;
            namespace TestModels;

            [RuleMessage("{PropertyName} {nope}")]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public class Request
            {
                [NotBlank] public string? Name { get; set; }
            }

            [PipelineBehavior(Order = 0, AppliesTo = typeof(Request))]
            public class SyncBehavior : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel instance,
                    System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(instance);
            }

            [PipelineBehavior(Order = 1, AppliesTo = typeof(Request))]
            public class AsyncBehavior : IPipelineBehavior
            {
                public static async ValueTask<ZeroAlloc.Validation.ValidationResult> Handle<TModel>(
                    TModel instance,
                    CancellationToken ct,
                    System.Func<TModel, CancellationToken, ValueTask<ZeroAlloc.Validation.ValidationResult>> next)
                    => await next(instance, ct);
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ValidateAsync", src, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(src, "ErrorMessage = \"Name {nope}\""));
        Assert.Equal(1, CountDiagnostics(result, "ZV0022"));
        var zv0022 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0022", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Warning, zv0022.Severity);
        Assert.Equal("NotBlank", SpanText(zv0022));
        Assert.Contains("'nope'", zv0022.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Builtin_messages_unchanged()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed class Request
            {
                [NotEmpty(Message = "{PropertyName} {minWords}")] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ErrorMessage = \"Name {minWords}\"", src, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZV0022", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_generic_ValidationAttribute_subclass_reports_ZV0020()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class NotBlankAttribute : ValidationAttribute { }

            [Validate]
            public sealed class Request
            {
                [NotBlank] public string? Name { get; init; }
            }
            """;

        var (result, _) = RunGenerator(source);

        Assert.Equal(1, CountDiagnostics(result, "ZV0020"));
        var zv0020 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0020", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, zv0020.Severity);
        Assert.Equal("NotBlank", SpanText(zv0020));
        Assert.Contains("'NotBlankAttribute'", zv0020.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ZV0020_reported_for_inherited_property()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class NotBlankAttribute : ValidationAttribute { }

            public class RequestBase
            {
                [NotBlank] public string? Name { get; init; }
            }

            [Validate]
            public sealed class Request : RequestBase { }
            """;

        var (result, _) = RunGenerator(source);

        Assert.Equal(1, CountDiagnostics(result, "ZV0020"));
    }

    [Fact]
    public void ZV0020_not_reported_for_builtins_or_generic_rules()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            [Validate]
            public sealed class Request
            {
                [NotEmpty, Must(nameof(Check)), NotBlank] public string? Name { get; init; }

                public bool Check(string? value) => value is not null;
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountDiagnostics(result, "ZV0020"));
    }

    [Fact]
    public void Type_mismatch_reports_ZV0021_and_skips_rule()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            [Validate]
            public sealed class Request
            {
                [NotBlank] public int Count { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(1, CountDiagnostics(result, "ZV0021"));
        var zv0021 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0021", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, zv0021.Severity);
        Assert.Equal("NotBlank", SpanText(zv0021));
        Assert.Equal(
            "'NotBlankAttribute' validates 'string?' but property 'Count' is 'int'",
            zv0021.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.DoesNotContain("__Rule_Count_", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Skipped_rule_is_reported_once_and_indices_follow_the_filtered_list()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            using ZeroAlloc.Pipeline;
            using System.Threading;
            using System.Threading.Tasks;
            namespace TestModels;

            {{NotBlankDeclaration}}

            public sealed class PositiveAttribute : ValidationAttribute<long>
            {
                public override bool IsValid(long value) => value > 0;
            }

            [Validate]
            public class Request
            {
                [NotBlank, Positive] public int Count { get; set; }
            }

            [PipelineBehavior(Order = 0, AppliesTo = typeof(Request))]
            public class SyncBehavior : IPipelineBehavior
            {
                public static ZeroAlloc.Validation.ValidationResult Handle<TModel>(
                    TModel instance,
                    System.Func<TModel, ZeroAlloc.Validation.ValidationResult> next)
                    => next(instance);
            }

            [PipelineBehavior(Order = 1, AppliesTo = typeof(Request))]
            public class AsyncBehavior : IPipelineBehavior
            {
                public static async ValueTask<ZeroAlloc.Validation.ValidationResult> Handle<TModel>(
                    TModel instance,
                    CancellationToken ct,
                    System.Func<TModel, CancellationToken, ValueTask<ZeroAlloc.Validation.ValidationResult>> next)
                    => await next(instance, ct);
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(1, CountDiagnostics(result, "ZV0021"));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ValidateAsync", src, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(src, "private static readonly global::TestModels.PositiveAttribute __Rule_Count_0"));
        Assert.Equal(2, CountOccurrences(src, "!__Rule_Count_0.IsValid("));
        Assert.DoesNotContain("__Rule_Count_1", src, StringComparison.Ordinal);
        Assert.DoesNotContain("NotBlankAttribute __Rule_", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Implicit_conversion_is_accepted()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class PositiveAttribute : ValidationAttribute<long>
            {
                public override bool IsValid(long value) => value > 0;
            }

            [Validate]
            public sealed class Request
            {
                [Positive] public int Count { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountDiagnostics(result, "ZV0021"));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("!__Rule_Count_0.IsValid(instance.Count)", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_value_to_non_nullable_is_rejected()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class PositiveAttribute : ValidationAttribute<int>
            {
                public override bool IsValid(int value) => value > 0;
            }

            [Validate]
            public sealed class Request
            {
                [Positive] public int? Count { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(1, CountDiagnostics(result, "ZV0021"));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.DoesNotContain("__Rule_Count_", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_nested_attribute_reports_ZV0023_and_emits_no_field()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public sealed class Request
            {
                private sealed class Hidden : ValidationAttribute<string?>
                {
                    public override bool IsValid(string? value) => value is not null;
                }

                [Hidden] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "Hidden", "TestModels.Request.Hidden", "Hidden");
    }

    [Fact]
    public void Protected_nested_attribute_reports_ZV0023_and_emits_no_field()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public class Request
            {
                protected sealed class Hidden : ValidationAttribute<string?>
                {
                    public override bool IsValid(string? value) => value is not null;
                }

                [Hidden] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "Hidden", "TestModels.Request.Hidden", "Hidden");
    }

    [Fact]
    public void Internal_and_protected_internal_nested_attributes_are_emitted()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [Validate]
            public class Request
            {
                internal sealed class Visible : ValidationAttribute<string?>
                {
                    public override bool IsValid(string? value) => value is not null;
                }

                protected internal sealed class AlsoVisible : ValidationAttribute<string?>
                {
                    public override bool IsValid(string? value) => value is not null;
                }

                [Visible, AlsoVisible] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountDiagnostics(result, "ZV0023"));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("__Rule_Name_0", src, StringComparison.Ordinal);
        Assert.Contains("__Rule_Name_1", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_nested_type_passed_as_typeof_reports_ZV0023()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class TypedAttribute : ValidationAttribute<string?>
            {
                public TypedAttribute(System.Type type) { }
                public System.Type[]? Types { get; set; }
                public override bool IsValid(string? value) => value is not null;
            }
            [Validate]
            public sealed class Request
            {
                private sealed class Secret { }

                [Typed(typeof(Secret))] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "TypedAttribute", "TestModels.Request.Secret", "Typed(typeof(Secret))");
    }

    [Fact]
    public void ZV0023_names_the_inaccessible_containing_type_of_a_typeof_operand()
    {
        // Inner is public, but it is nested in a private type, so the message names Outer.
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class TypedAttribute : ValidationAttribute<string?>
            {
                public TypedAttribute(System.Type type) { }
                public System.Type[]? Types { get; set; }
                public override bool IsValid(string? value) => value is not null;
            }
            [Validate]
            public sealed class Request
            {
                private sealed class Outer
                {
                    public sealed class Inner { }
                }

                [Typed(typeof(Outer.Inner))] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "TypedAttribute", "TestModels.Request.Outer", "Typed(typeof(Outer.Inner))");
    }

    [Fact]
    public void Private_nested_type_inside_array_of_types_reports_ZV0023()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class TypedAttribute : ValidationAttribute<string?>
            {
                public TypedAttribute(System.Type type) { }
                public System.Type[]? Types { get; set; }
                public override bool IsValid(string? value) => value is not null;
            }
            [Validate]
            public sealed class Request
            {
                private sealed class Secret { }

                [Typed(typeof(string), Types = new[] { typeof(int), typeof(Secret) })] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "TypedAttribute", "TestModels.Request.Secret", "Typed(typeof(string), Types = new[] { typeof(int), typeof(Secret) })");
    }

    [Fact]
    public void Private_nested_type_as_generic_argument_of_typeof_reports_ZV0023()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class TypedAttribute : ValidationAttribute<string?>
            {
                public TypedAttribute(System.Type type) { }
                public System.Type[]? Types { get; set; }
                public override bool IsValid(string? value) => value is not null;
            }
            [Validate]
            public sealed class Request
            {
                private sealed class Secret { }

                [Typed(typeof(System.Collections.Generic.Dictionary<string, Secret[]>))] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "TypedAttribute", "TestModels.Request.Secret", "Typed(typeof(System.Collections.Generic.Dictionary<string, Secret[]>))");
    }

    [Fact]
    public void Private_nested_enum_passed_as_object_reports_ZV0023()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class TaggedAttribute : ValidationAttribute<string?>
            {
                public TaggedAttribute(object tag) { }
                public override bool IsValid(string? value) => value is not null;
            }

            [Validate]
            public sealed class Request
            {
                private enum Mode { A }

                [Tagged(Mode.A)] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "TaggedAttribute", "TestModels.Request.Mode", "Tagged(Mode.A)");
    }

    [Fact]
    public void Accessible_typeof_operands_are_emitted()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class TypedAttribute : ValidationAttribute<string?>
            {
                public TypedAttribute(System.Type type) { }
                public System.Type[]? Types { get; set; }
                public override bool IsValid(string? value) => value is not null;
            }
            [Validate]
            public sealed class Request
            {
                internal sealed class Visible { }

                [Typed(typeof(System.Collections.Generic.List<Visible>), Types = new[] { typeof(System.Collections.Generic.List<>) })]
                public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountDiagnostics(result, "ZV0023"));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("__Rule_Name_0", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_constructor_reached_from_nested_model_reports_ZV0023()
    {
        // The model is nested inside the attribute so the usage can bind a private constructor.
        // The compile is not clean for a reason unrelated to ZV0023: a validator for a model
        // nested in another type names it unqualified and fails with CS0246, tracked in #207.
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class GuardAttribute : ValidationAttribute<string?>
            {
                private GuardAttribute() { }
                public GuardAttribute(int unused) { }
                public override bool IsValid(string? value) => value is not null;

                [Validate]
                public sealed class Request
                {
                    [Guard] public string? Name { get; init; }
                }
            }
            """;

        AssertZV0023AndNoField(source, "GuardAttribute", "TestModels.GuardAttribute.GuardAttribute()", "Guard", requireCleanCompile: false);
    }

    [Fact]
    public void Nullable_property_with_non_nullable_rule_reports_ZV0021()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class StrictAttribute : ValidationAttribute<string>
            {
                public override bool IsValid(string value) => value.Length > 0;
            }

            [Validate]
            public sealed class Request
            {
                [Strict] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountCompilerDiagnostics(output, "CS8604"));
        Assert.Equal(1, CountDiagnostics(result, "ZV0021"));
        var zv0021 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0021", StringComparison.Ordinal));
        Assert.Equal(
            "'StrictAttribute' validates 'string' but property 'Name' is 'string?'. "
                + "Declare the rule as ValidationAttribute<string?> to accept null.",
            zv0021.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.DoesNotContain("__Rule_", src, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("string?", "string?", "")]
    [InlineData("string", "string?", "")]
    [InlineData("string", "string", "#nullable disable")]
    public void Nullability_compatible_rules_are_emitted(string propertyType, string valueType, string pragma)
    {
        var source = $$"""
            {{pragma}}
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class RuleAttribute : ValidationAttribute<{{valueType}}>
            {
                public override bool IsValid({{valueType}} value) => value is not null;
            }

            [Validate]
            public sealed class Request
            {
                [Rule] public {{propertyType}} Name { get; init; } = "";
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountCompilerDiagnostics(output, "CS8604"));
        Assert.Equal(0, CountDiagnostics(result, "ZV0021"));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("!__Rule_Name_0.IsValid(", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Unresolved_property_type_skips_rule_without_ZV0021()
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{NotBlankDeclaration}}

            [Validate]
            public sealed class Request
            {
                [NotBlank] public Missing? Name { get; init; }
            }
            """;

        var (result, _) = RunGenerator(source);

        Assert.Equal(0, CountDiagnostics(result, "ZV0021"));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.DoesNotContain("__Rule_", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Unresolved_rule_value_type_skips_rule_without_ZV0021()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class BrokenAttribute : ValidationAttribute<Missing>
            {
                public override bool IsValid(Missing value) => true;
            }

            [Validate]
            public sealed class Request
            {
                [Broken] public string? Name { get; init; }
            }
            """;

        var (result, _) = RunGenerator(source);

        Assert.Equal(0, CountDiagnostics(result, "ZV0021"));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.DoesNotContain("__Rule_", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_rule_on_field_reports_ZV0024()
    {
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed class Request
            {
                [NotBlank] public string? Name;
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        AssertSingleZV0024(result, "NotBlankAttribute", "Name");
    }

    [Fact]
    public void Custom_rule_on_record_positional_parameter_reports_ZV0024()
    {
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed record Request([NotBlank] string? Name);
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        AssertSingleZV0024(result, "NotBlankAttribute", "Name");
        Assert.DoesNotContain("__Rule_", GetGeneratedSource(result, "RequestValidator.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_rule_with_property_target_on_record_parameter_is_emitted_without_ZV0024()
    {
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed record Request([property: NotBlank] string? Name);
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        Assert.Equal(0, CountDiagnostics(result, "ZV0024"));
        Assert.Contains("!__Rule_Name_0.IsValid(instance.Name)", GetGeneratedSource(result, "RequestValidator.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Non_generic_ValidationAttribute_subclass_on_parameter_reports_ZV0024_not_ZV0020()
    {
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
            public sealed class LegacyAttribute : ValidationAttribute { }

            [Validate]
            public sealed class Request
            {
                public Request([Legacy] string? name) => Name = name;

                public string? Name { get; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        AssertSingleZV0024(result, "LegacyAttribute", "name", "Legacy");
        Assert.Equal(0, CountDiagnostics(result, "ZV0020"));
    }

    [Fact]
    public void Field_on_plain_base_type_reports_ZV0024()
    {
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            public class RequestBase
            {
                [NotBlank] private string? _code;
                public string? Code => _code;
            }

            [Validate]
            public sealed class Request : RequestBase
            {
                public string? Name { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        AssertSingleZV0024(result, "NotBlankAttribute", "_code");
    }

    [Fact]
    public void Field_on_Validate_base_type_reports_ZV0024_once()
    {
        var source = """
            using System;
            using ZeroAlloc.Validation;
            namespace TestModels;

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public class RequestBase
            {
                [NotBlank] public string? Code;
            }

            [Validate]
            public sealed class Request : RequestBase
            {
                public string? Name { get; set; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        AssertSingleZV0024(result, "NotBlankAttribute", "Code");
    }

    [Fact]
    public void File_local_attribute_reports_ZV0023_and_emits_no_field()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            file sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed class Request
            {
                [NotBlank] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "NotBlankAttribute", "TestModels.NotBlankAttribute", "NotBlank");
    }

    [Fact]
    public void Custom_rule_nested_in_file_local_type_reports_ZV0023()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            file static class Rules
            {
                public sealed class NotBlankAttribute : ValidationAttribute<string?>
                {
                    public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
                }
            }

            [Validate]
            public sealed class Request
            {
                [Rules.NotBlank] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "NotBlankAttribute", "TestModels.Rules", "Rules.NotBlank");
    }

    [Fact]
    public void File_local_type_passed_as_typeof_reports_ZV0023()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            file sealed class Secret { }

            public sealed class TypedAttribute(System.Type type) : ValidationAttribute<string?>
            {
                public System.Type Type { get; } = type;
                public override bool IsValid(string? value) => value is not null;
            }

            [Validate]
            public sealed class Request
            {
                [Typed(typeof(Secret))] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "TypedAttribute", "TestModels.Secret", "Typed(typeof(Secret))");
    }

    [Fact]
    public void File_local_enum_passed_as_object_reports_ZV0023()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            file enum Mode { On = 1 }

            public sealed class BoxedAttribute(object value) : ValidationAttribute<string?>
            {
                public object Value { get; } = value;
                public override bool IsValid(string? value) => value is not null;
            }

            [Validate]
            public sealed class Request
            {
                [Boxed(Mode.On)] public string? Name { get; init; }
            }
            """;

        AssertZV0023AndNoField(source, "BoxedAttribute", "TestModels.Mode", "Boxed(Mode.On)");
    }

    [Fact]
    public void Explicit_null_ErrorCode_on_usage_clears_RuleMessage_error_code()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            [RuleMessage("{PropertyName} must not be blank.", ErrorCode = "NOT_BLANK")]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed class Request
            {
                [NotBlank(ErrorCode = null)] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Contains("ErrorMessage = \"Name must not be blank.\" }", src, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT_BLANK", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Member_hiding_a_base_member_is_copied_into_the_instance()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class TaggedAttribute : ValidationAttribute<string?>
            {
                public new string? Message { get; set; }
                public new string? When { get; set; }
                public int Tag { get; set; }
                public override bool IsValid(string? value) => value != Message;
            }

            [Validate]
            public sealed class Request
            {
                [Tagged(Message = "custom", When = "NotAMethod", Tag = 3, ErrorCode = "E")]
                public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.Equal(
            "= new global::TestModels.TaggedAttribute() { Message = \"custom\", When = \"NotAMethod\", Tag = 3 };",
            FieldInitializer(src, "__Rule_Name_0"));
        Assert.Contains("ErrorMessage = \"Name is invalid.\", ErrorCode = \"E\"", src, StringComparison.Ordinal);
        Assert.DoesNotContain("NotAMethod()", src, StringComparison.Ordinal);
        Assert.Equal(0, CountDiagnostics(result, "ZV0017"));
    }

    [Theory]
    [InlineData("[System.Obsolete(\"Use NewRule.\")]", "")]
    [InlineData("[System.Obsolete]", "")]
    [InlineData("", "[System.Obsolete(\"Use the other constructor.\")]")]
    public void Obsolete_rule_field_is_wrapped_in_a_pragma(string typeAttribute, string constructorAttribute)
    {
        var source = $$"""
            using ZeroAlloc.Validation;
            namespace TestModels;

            {{typeAttribute}}
            public sealed class OldRuleAttribute : ValidationAttribute<string?>
            {
                {{constructorAttribute}}
                public OldRuleAttribute() { }
                public override bool IsValid(string? value) => value is not null;
            }

            [Validate]
            public sealed class Request
            {
            #pragma warning disable CS0612, CS0618
                [OldRule] public string? Name { get; init; }
            #pragma warning restore CS0612, CS0618
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var generatedTree = result.GeneratedTrees.First(t => t.FilePath.EndsWith("RequestValidator.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(
            output.GetDiagnostics(),
            d => d.Location.SourceTree == generatedTree && d.Id is "CS0618" or "CS0612");
        var src = NormalizeNewLines(generatedTree.ToString());
        Assert.Contains(
            "\n    // The rule type is obsolete; the compiler already warns at the attribute usage in user code.\n"
                + "#pragma warning disable CS0618, CS0612\n"
                + "    private static readonly global::TestModels.OldRuleAttribute __Rule_Name_0\n"
                + "        = new global::TestModels.OldRuleAttribute();\n"
                + "#pragma warning restore CS0618, CS0612\n",
            src,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(src, "#pragma warning disable CS0618"));
    }

    [Fact]
    public void Non_obsolete_rule_field_has_no_pragma()
    {
        var source = """
            using ZeroAlloc.Validation;
            namespace TestModels;

            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public sealed class Request
            {
                [NotBlank] public string? Name { get; init; }
            }
            """;

        var (result, output) = RunGenerator(source);

        Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.DoesNotContain("CS0618", src, StringComparison.Ordinal);
        Assert.DoesNotContain("obsolete", src, StringComparison.Ordinal);
    }

    private static void AssertSingleZV0024(GeneratorDriverRunResult result, string attributeName, string targetName, string? spanText = null)
    {
        Assert.Equal(1, CountDiagnostics(result, "ZV0024"));
        var zv0024 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0024", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, zv0024.Severity);
        Assert.Equal(spanText ?? "NotBlank", SpanText(zv0024));
        Assert.Equal(
            $"'{attributeName}' is applied to '{targetName}', which the generator does not validate; apply it to a property",
            zv0024.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AssertZV0023AndNoField(
        string source,
        string attributeName,
        string inaccessibleSymbol,
        string spanText,
        bool requireCleanCompile = true)
    {
        var (result, output) = RunGenerator(source);

        Assert.Equal(1, CountDiagnostics(result, "ZV0023"));
        var zv0023 = result.Diagnostics.First(d => string.Equals(d.Id, "ZV0023", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, zv0023.Severity);
        Assert.Equal(spanText, SpanText(zv0023));
        Assert.Equal(
            $"'{attributeName}' cannot be emitted: '{inaccessibleSymbol}' is not accessible from the generated validator; make it internal or public",
            zv0023.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(output.GetDiagnostics(), d => string.Equals(d.Id, "CS0122", StringComparison.Ordinal));
        if (requireCleanCompile)
            Assert.Empty(CompileErrors(output));
        var src = GetGeneratedSource(result, "RequestValidator.g.cs");
        Assert.DoesNotContain("__Rule_", src, StringComparison.Ordinal);
    }

    private static int CountCompilerDiagnostics(Compilation output, string id)
    {
        var count = 0;
        foreach (var diagnostic in output.GetDiagnostics())
        {
            if (string.Equals(diagnostic.Id, id, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    private static string SpanText(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static int CountDiagnostics(GeneratorDriverRunResult result, string id)
    {
        var count = 0;
        foreach (var diagnostic in result.Diagnostics)
        {
            if (string.Equals(diagnostic.Id, id, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    private static string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FieldInitializer(string src, string fieldName)
    {
        var lines = src.Split('\n');
        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (lines[i].TrimEnd().EndsWith(" " + fieldName, StringComparison.Ordinal)
                && lines[i].Contains("private static readonly", StringComparison.Ordinal))
            {
                return lines[i + 1].Trim();
            }
        }

        throw new InvalidOperationException($"Field {fieldName} not found in:\n{src}");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static List<Diagnostic> CompileErrors(Compilation output)
    {
        var errors = new List<Diagnostic>();
        foreach (var diagnostic in output.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
                errors.Add(diagnostic);
        }
        return errors;
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult result, string filenameSuffix) =>
        result.GeneratedTrees
            .First(t => t.FilePath.EndsWith(filenameSuffix, StringComparison.Ordinal))
            .ToString();

    private static (GeneratorDriverRunResult Result, Compilation Output) RunGenerator(string source)
    {
        var driver = CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(CreateCompilation(source), out var output, out _);
        return (driver.GetRunResult(), output);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var valueObjectStub = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }
            """;

        // Ensure ZeroAlloc.Pipeline is loaded so its assembly is referenced for behavior models.
        _ = typeof(ZeroAlloc.Pipeline.IPipelineBehavior).Assembly;

        return CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(valueObjectStub)],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
