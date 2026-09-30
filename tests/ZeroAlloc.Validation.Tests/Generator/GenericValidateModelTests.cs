using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.Validation.Generator;
using static ZeroAlloc.Validation.Tests.Generator.GenericModelTestHost;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// Generic <c>[Validate]</c> models, issue #238, phase 1: the generated validator is generic over
/// the type parameters of the model and of every type containing it, with their declared names
/// and constraints, its rules on a type parameter follow the constraints, and it composes the
/// closings its properties hold. Every case compiles the generated code with the nullable context
/// and XML documentation on.
/// </summary>
public class GenericValidateModelTests
{
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using ZeroAlloc.Validation;
        namespace Ns;

        """;

    private static Output RunModel(string declarations, params IIncrementalGenerator[] generators) =>
        Run(Header + declarations, generators);

    // ---------------------------------------------------------------- shape

    public static TheoryData<string, string, string> Shapes() => new()
    {
        {
            "[Validate] public class Box<T> { [NotEmpty] public string Name { get; set; } = \"\"; }",
            "Ns.BoxValidator`1.g.cs",
            "public sealed partial class BoxValidator<T> : ValidatorFor<global::Ns.Box<T>>"
        },
        {
            "[Validate] public class Pair<TKey, TValue> { [NotEmpty] public string Name { get; set; } = \"\"; }",
            "Ns.PairValidator`2.g.cs",
            "public sealed partial class PairValidator<TKey, TValue> : ValidatorFor<global::Ns.Pair<TKey, TValue>>"
        },
        {
            "[Validate] public class Triple<A, B, C> { [NotEmpty] public string Name { get; set; } = \"\"; }",
            "Ns.TripleValidator`3.g.cs",
            "public sealed partial class TripleValidator<A, B, C> : ValidatorFor<global::Ns.Triple<A, B, C>>"
        },
        {
            "public class Envelope<T> { [Validate] public class Header { [NotEmpty] public string Name { get; set; } = \"\"; } }",
            "Ns.Envelope_HeaderValidator`1.g.cs",
            "public sealed partial class Envelope_HeaderValidator<T> : ValidatorFor<global::Ns.Envelope<T>.Header>"
        },
        {
            "public class Envelope<T> { [Validate] public class Part<U> { [NotEmpty] public string Name { get; set; } = \"\"; } }",
            "Ns.Envelope_PartValidator`2.g.cs",
            "public sealed partial class Envelope_PartValidator<T, U> : ValidatorFor<global::Ns.Envelope<T>.Part<U>>"
        },
        {
            "public class Outer<T> { public class Mid { [Validate] internal class Inner<U> { [NotEmpty] public string Name { get; set; } = \"\"; } } }",
            "Ns.Outer_Mid_InnerValidator`2.g.cs",
            "internal sealed partial class Outer_Mid_InnerValidator<T, U> : ValidatorFor<global::Ns.Outer<T>.Mid.Inner<U>>"
        },
        {
            "[Validate] public record Rec<T>(string Name) { [NotEmpty] public string Code { get; init; } = \"\"; }",
            "Ns.RecValidator`1.g.cs",
            "public sealed partial class RecValidator<T> : ValidatorFor<global::Ns.Rec<T>>"
        },
        {
            "[Validate] public readonly struct Cell<T> { [NotEmpty] public string Name { get; init; } }",
            "Ns.CellValidator`1.g.cs",
            "public sealed partial class CellValidator<T> : ValidatorFor<global::Ns.Cell<T>>"
        },
        {
            "[Validate] public readonly record struct Slot<T>([property: NotEmpty] string Name);",
            "Ns.SlotValidator`1.g.cs",
            "public sealed partial class SlotValidator<T> : ValidatorFor<global::Ns.Slot<T>>"
        },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void GenericModel_GetsAGenericValidator(string declaration, string hintName, string declarationLine)
    {
        var output = RunModel(declaration).Compiles();

        Assert.Empty(output.Diagnostics);
        var source = output.Source(hintName);
        Assert.Contains(declarationLine, source, StringComparison.Ordinal);

        // One <typeparam> per type parameter, all or none: CS1712 otherwise. Compiles() has
        // already ruled out CS1570 and CS1712 under DocumentationMode.Diagnose.
        var typeParameters = declarationLine.Substring(declarationLine.IndexOf('<') + 1);
        typeParameters = typeParameters.Substring(0, typeParameters.IndexOf('>'));
        foreach (var name in typeParameters.Split(", "))
            Assert.Contains($"/// <typeparam name=\"{name}\">", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericValidator_IsFoundByItsMetadataName()
    {
        var output = RunModel("""
            [Validate] public class Box<T> { [NotEmpty] public string Name { get; set; } = ""; }
            public class Envelope<T> { [Validate] public class Header { [NotEmpty] public string Name { get; set; } = ""; } }
            """).Compiles();

        Assert.NotNull(output.Compilation.GetTypeByMetadataName("Ns.BoxValidator`1"));
        Assert.NotNull(output.Compilation.GetTypeByMetadataName("Ns.Envelope_HeaderValidator`1"));
    }

    public static TheoryData<string, string> Constraints() => new()
    {
        { "where T : class", "where T : class" },
        { "where T : class?", "where T : class?" },
        { "where T : struct", "where T : struct" },
        { "where T : unmanaged", "where T : unmanaged" },
        { "where T : notnull", "where T : notnull" },
        { "where T : new()", "where T : new()" },
        { "where T : class, new()", "where T : class, new()" },
        { "where T : IComparable<T>", "where T : global::System.IComparable<T>" },
        { "where T : IComparable<T>?", "where T : global::System.IComparable<T>?" },
        { "where T : Base", "where T : global::Ns.Base" },
        { "where T : Base, IDisposable, new()", "where T : global::Ns.Base, global::System.IDisposable, new()" },
#if NET9_0_OR_GREATER
        // allows ref struct needs a runtime with by-ref-like generics, which net8.0 lacks.
        { "where T : allows ref struct", "where T : allows ref struct" },
        { "where T : IDisposable, allows ref struct", "where T : global::System.IDisposable, allows ref struct" },
#endif
        { "where T : struct, Enum", "where T : struct, global::System.Enum" },
    };

    [Theory]
    [MemberData(nameof(Constraints))]
    public void GenericValidator_CopiesTheConstraints(string declared, string emitted)
    {
        var output = RunModel($$"""
            public class Base { }

            [Validate]
            public class Box<T> {{declared}}
            {
                [NotEmpty] public string Name { get; set; } = "";
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains(
            "public sealed partial class BoxValidator<T> : ValidatorFor<global::Ns.Box<T>>\n    " + emitted + "\n{",
            output.Source("Ns.BoxValidator`1.g.cs").Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void GenericValidator_CopiesAConstraintNamingAnotherTypeParameter()
    {
        var output = RunModel("""
            [Validate]
            public class Range<TValue, TBound> where TBound : TValue where TValue : IComparable<TValue>
            {
                [NotEmpty] public string Name { get; set; } = "";
            }

            public class Outer<T> where T : class
            {
                [Validate]
                public class Inner<U> where U : T
                {
                    [NotEmpty] public string Name { get; set; } = "";
                }
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        var range = output.Source("Ns.RangeValidator`2.g.cs");
        Assert.Contains("    where TValue : global::System.IComparable<TValue>", range, StringComparison.Ordinal);
        Assert.Contains("    where TBound : TValue", range, StringComparison.Ordinal);
        var inner = output.Source("Ns.Outer_InnerValidator`2.g.cs");
        Assert.Contains("    where T : class", inner, StringComparison.Ordinal);
        Assert.Contains("    where U : T", inner, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- names

    [Fact]
    public void GenericAndNonGenericModelOfTheSameName_GetTwoValidators()
    {
        var output = RunModel("""
            [Validate] public class Box { [NotEmpty] public string Name { get; set; } = ""; }
            [Validate] public class Box<T> { [NotEmpty] public string Name { get; set; } = ""; }
            [Validate] public class Box<T, U> { [NotEmpty] public string Name { get; set; } = ""; }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains("class BoxValidator : ", output.Source("Ns.BoxValidator.g.cs"), StringComparison.Ordinal);
        Assert.Contains("class BoxValidator<T> : ", output.Source("Ns.BoxValidator`1.g.cs"), StringComparison.Ordinal);
        Assert.Contains("class BoxValidator<T, U> : ", output.Source("Ns.BoxValidator`2.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ModelInAGenericContainer_BesideATopLevelGenericModelOfTheJoinedName_ReportsZV0031()
    {
        // Both validators would be Outer_InnerValidator<T>.
        var output = RunModel("""
            public class Outer<T> { [Validate] public class Inner { [NotEmpty] public string Name { get; set; } = ""; } }
            [Validate] public class Outer_Inner<T> { [NotEmpty] public string Name { get; set; } = ""; }
            """).Compiles();

        Assert.Equal(["ZV0031", "ZV0031"], output.Ids());
        Assert.DoesNotContain(output.Generated, g => g.HintName.Contains("Outer_InnerValidator", StringComparison.Ordinal));
    }

    [Fact]
    public void ModelInAGenericContainer_BesideANonGenericModelOfTheJoinedName_DoesNotClash()
    {
        // Outer_InnerValidator<T> and Outer_InnerValidator differ in arity.
        var output = RunModel("""
            public class Outer<T> { [Validate] public class Inner { [NotEmpty] public string Name { get; set; } = ""; } }
            [Validate] public class Outer_Inner { [NotEmpty] public string Name { get; set; } = ""; }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains(output.Generated, g => string.Equals(g.HintName, "Ns.Outer_InnerValidator`1.g.cs", StringComparison.Ordinal));
        Assert.Contains(output.Generated, g => string.Equals(g.HintName, "Ns.Outer_InnerValidator.g.cs", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- rules on a type parameter

    [Theory]
    [InlineData("")]
    [InlineData("where T : IConvertible")]
    [InlineData("where T : IComparable<T>")]
    public void NumericRuleOnATypeParameterThatIsNotANumber_ReportsZV0033(string constraint)
    {
        var output = RunModel($$"""
            [Validate]
            public class Box<T> {{constraint}}
            {
                [GreaterThan(0)] public T Value { get; set; } = default!;
            }
            """).Compiles();

        var zv0033 = output.OnlyDiagnostic();
        Assert.Equal("ZV0033", zv0033.Id);
        Assert.Equal(DiagnosticSeverity.Error, zv0033.Severity);
        Assert.Equal("GreaterThan(0)", SourceAt(zv0033));
        Assert.Equal(
            "'GreaterThanAttribute' compares 'Value' as a number, but its type 'T' cannot be converted to one; "
                + "constrain it to System.Numerics.INumberBase<T>, or use [Must] or a custom ValidationAttribute<T> to compare it",
            zv0033.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain("Value", output.Source("Ns.BoxValidator`1.g.cs").Split("Validate(")[1], StringComparison.Ordinal);
    }

    [Fact]
    public void NumericRuleOnATypeThatIsNotANumber_KeepsItsMessage()
    {
        // The hint only changes for a type parameter.
        var output = RunModel("""
            [Validate]
            public class Booking { [GreaterThan(0)] public DateTime When { get; set; } }
            """);

        var zv0033 = output.OnlyDiagnostic();
        Assert.Equal(
            "'GreaterThanAttribute' compares 'When' as a number, but its type 'System.DateTime' cannot be converted to one; "
                + "use [Must] or a custom ValidationAttribute<T> to compare it",
            zv0033.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static TheoryData<string, string, string> NumericTypeParameterRules() => new()
    {
        // A possibly-null T is guarded before CreateChecked.
        { "where T : INumber<T>", "T", "instance.Value is not null && (double.CreateChecked(instance.Value) <= 0)" },
        { "where T : INumber<T>", "T?", "instance.Value is not null && (double.CreateChecked(instance.Value) <= 0)" },
        { "where T : INumberBase<T>", "T", "instance.Value is not null && (double.CreateChecked(instance.Value) <= 0)" },
        { "where T : System.Numerics.IBinaryInteger<T>", "T", "instance.Value is not null && (double.CreateChecked(instance.Value) <= 0)" },
        // A struct or unmanaged T cannot be null, so it is not guarded.
        { "where T : struct, INumber<T>", "T", "double.CreateChecked(instance.Value) <= 0" },
        { "where T : unmanaged, INumber<T>", "T", "double.CreateChecked(instance.Value) <= 0" },
        // A Nullable<T> is compared on its value, as for int?.
        { "where T : struct, INumber<T>", "T?", "instance.Value.HasValue && (double.CreateChecked(instance.Value.Value) <= 0)" },
    };

    [Theory]
    [MemberData(nameof(NumericTypeParameterRules))]
    public void NumericRuleOnAGenericNumber_ComparesThroughCreateChecked(string constraint, string type, string condition)
    {
        var output = RunModel($$"""
            using System.Numerics;

            [Validate]
            public class Amount<T> {{constraint}}
            {
                [GreaterThan(0)] public {{type}} Value { get; set; } = default!;
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains($"if ({condition})", output.Source("Ns.AmountValidator`1.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNumericRuleOnAGenericNumber_ComparesThroughCreateChecked()
    {
        var output = RunModel("""
            using System.Numerics;

            [Validate]
            public class Amount<T> where T : struct, INumber<T>
            {
                [GreaterThanOrEqualTo(1)] [LessThan(10)] [LessThanOrEqualTo(9)] public T A { get; set; }
                [InclusiveBetween(1, 5)] [ExclusiveBetween(0, 6)] public T B { get; set; }
                [Equal(3)] [NotEqual(4)] public T C { get; set; }
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        var source = output.Source("Ns.AmountValidator`1.g.cs");
        Assert.Contains("if (double.CreateChecked(instance.A) < 1)", source, StringComparison.Ordinal);
        Assert.Contains("if (double.CreateChecked(instance.A) >= 10)", source, StringComparison.Ordinal);
        Assert.Contains("if (double.CreateChecked(instance.A) > 9)", source, StringComparison.Ordinal);
        Assert.Contains("if (double.CreateChecked(instance.B) < 1 || double.CreateChecked(instance.B) > 5)", source, StringComparison.Ordinal);
        Assert.Contains("if (double.CreateChecked(instance.B) <= 0 || double.CreateChecked(instance.B) >= 6)", source, StringComparison.Ordinal);
        Assert.Contains("if (double.CreateChecked(instance.C) != 3)", source, StringComparison.Ordinal);
        Assert.Contains("if (double.CreateChecked(instance.C) == 4)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Convert.ToDouble", source, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> RulesWithoutATypeParameterForm() => new()
    {
        { "NotEmpty", "NotEmptyAttribute" },
        { "Empty", "EmptyAttribute" },
        { "MinLength(1)", "MinLengthAttribute" },
        { "MaxLength(5)", "MaxLengthAttribute" },
        { "Length(1, 5)", "LengthAttribute" },
        { "EmailAddress", "EmailAddressAttribute" },
        { "Matches(\"^a$\")", "MatchesAttribute" },
        { "IsEnumName(typeof(DayOfWeek))", "IsEnumNameAttribute" },
        { "PrecisionScale(5, 2)", "PrecisionScaleAttribute" },
        { "Equal(\"a\")", "EqualAttribute" },
        { "NotEqual(\"a\")", "NotEqualAttribute" },
        { "IsInEnum", "IsInEnumAttribute" },
    };

    [Theory]
    [MemberData(nameof(RulesWithoutATypeParameterForm))]
    public void RuleWithoutATypeParameterForm_ReportsZV0036(string rule, string attributeName)
    {
        var output = RunModel($$"""
            [Validate]
            public class Box<T>
            {
                [{{rule}}] public T Value { get; set; } = default!;
                [NotEmpty] public string Name { get; set; } = "";
            }
            """).Compiles();

        var zv0036 = output.OnlyDiagnostic();
        Assert.Equal("ZV0036", zv0036.Id);
        Assert.Equal(DiagnosticSeverity.Error, zv0036.Severity);
        Assert.Equal(rule, SourceAt(zv0036));
        Assert.Equal(
            $"'{attributeName}' cannot validate 'Value': its type 'T' is a type parameter, so the rule has no form "
                + "that fits every closing; constrain the type parameter, use [Must] or a custom ValidationAttribute<T>",
            zv0036.GetMessage(System.Globalization.CultureInfo.InvariantCulture));

        // The rule is left out; the other property's rule stays.
        var body = output.Source("Ns.BoxValidator`1.g.cs").Split("Validate(")[1];
        Assert.DoesNotContain("instance.Value", body, StringComparison.Ordinal);
        Assert.Contains("instance.Name", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleWithoutATypeParameterForm_OnANullableTypeParameter_ReportsZV0036()
    {
        var output = RunModel("""
            [Validate]
            public class Box<T> where T : struct
            {
                [NotEmpty] public T? Value { get; set; }
            }
            """).Compiles();

        Assert.Equal(["ZV0036"], output.Ids());
    }

    [Theory]
    [InlineData("T", "if (!global::System.Enum.IsDefined<T>(instance.Kind))")]
    [InlineData("T?", "if (instance.Kind.HasValue && !global::System.Enum.IsDefined<T>(instance.Kind.Value))")]
    public void IsInEnumOnAnEnumTypeParameter_UsesTheGenericIsDefined(string type, string condition)
    {
        var output = RunModel($$"""
            [Validate]
            public class Flagged<T> where T : struct, Enum
            {
                [IsInEnum] public {{type}} Kind { get; set; }
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains(condition, output.Source("Ns.FlaggedValidator`1.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void IsInEnumOnAGenericValueObjectOfAnEnumTypeParameter_ChecksItsMember()
    {
        var output = RunModel("""
            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly record struct Wrapped<T>(T Value) where T : struct, Enum;

            [Validate]
            public class Flagged<T> where T : struct, Enum
            {
                [IsInEnum] public Wrapped<T> Kind { get; set; }
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains("if (!global::System.Enum.IsDefined<T>(instance.Kind.Value))", output.Source("Ns.FlaggedValidator`1.g.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("where T : class")]
    [InlineData("where T : struct")]
    public void NullRulesOnATypeParameter_AreEmitted(string constraint)
    {
        var output = RunModel($$"""
            [Validate]
            public class Box<T> {{constraint}}
            {
                [NotNull] public T? Required { get; set; }
                [Null] public T? Absent { get; set; }
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        var source = output.Source("Ns.BoxValidator`1.g.cs");
        Assert.Contains("if (instance.Required is null)", source, StringComparison.Ordinal);
        Assert.Contains("if (instance.Absent is not null)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MustOnATypeParameter_CallsThePredicateWithTheValue()
    {
        var output = RunModel("""
            [Validate]
            public class Box<T>
            {
                [Must(nameof(IsKnown))] public T Value { get; set; } = default!;

                public bool IsKnown(T value) => value is not null;
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains("if (!instance.IsKnown(instance.Value))", output.Source("Ns.BoxValidator`1.g.cs"), StringComparison.Ordinal);
    }

    private const string CustomRules = """
        [AttributeUsage(AttributeTargets.Property)]
        public sealed class PresentAttribute : ValidationAttribute<object?>
        {
            public override bool IsValid(object? value) => value is not null;
        }

        [AttributeUsage(AttributeTargets.Property)]
        public sealed class PresentStrictAttribute : ValidationAttribute<object>
        {
            public override bool IsValid(object value) => true;
        }

        public interface ICoded { string Code { get; } }

        [AttributeUsage(AttributeTargets.Property)]
        public sealed class CodedAttribute : ValidationAttribute<ICoded?>
        {
            public override bool IsValid(ICoded? value) => value is null || value.Code.Length > 0;
        }

        [AttributeUsage(AttributeTargets.Property)]
        public sealed class PositiveAttribute : ValidationAttribute<int>
        {
            public override bool IsValid(int value) => value > 0;
        }

        """;

    [Fact]
    public void CustomRuleOnATypeParameter_IsCalledWhenTheTypeParameterConvertsToItsValueType()
    {
        var output = RunModel(CustomRules + """
            [Validate]
            public class Box<T> where T : ICoded
            {
                [Present] [Coded] public T Value { get; set; } = default!;
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        var source = output.Source("Ns.BoxValidator`1.g.cs");
        Assert.Contains("!__Rule_Value_0.IsValid(instance.Value)", source, StringComparison.Ordinal);
        Assert.Contains("!__Rule_Value_1.IsValid(instance.Value)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomRuleOnATypeParameter_ThatDoesNotConvert_ReportsZV0021()
    {
        var output = RunModel(CustomRules + """
            [Validate]
            public class Box<T>
            {
                [Positive] public T Value { get; set; } = default!;
            }
            """).Compiles();

        var zv0021 = output.OnlyDiagnostic();
        Assert.Equal("ZV0021", zv0021.Id);
        Assert.Equal("'PositiveAttribute' validates 'int' but property 'Value' is 'T'", zv0021.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("")]
    [InlineData("where T : class")]
    [InlineData("where T : notnull")]
    public void CustomRuleThatNeverReceivesNull_OnAPossiblyNullTypeParameter_ReportsZV0021(string constraint)
    {
        // Any closing but a value type may put null in the property, a Page<string?> for one, so
        // a rule declared never to receive null is not called with it.
        var output = RunModel(CustomRules + $$"""
            [Validate]
            public class Box<T> {{constraint}}
            {
                [PresentStrict] public T Value { get; set; } = default!;
            }
            """).Compiles();

        var zv0021 = output.OnlyDiagnostic();
        Assert.Equal("ZV0021", zv0021.Id);
        Assert.Equal(
            "'PresentStrictAttribute' validates 'object' but property 'Value' is 'T'. Declare the rule as ValidationAttribute<object?> to accept null.",
            zv0021.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CustomRuleThatNeverReceivesNull_OnAStructTypeParameter_IsCalled()
    {
        var output = RunModel(CustomRules + """
            [Validate]
            public class Box<T> where T : struct
            {
                [PresentStrict] public T Value { get; set; }
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains("!__Rule_Value_0.IsValid(instance.Value)", output.Source("Ns.BoxValidator`1.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void PropertyValuePlaceholderOnATypeParameter_TestsForNullFirst()
    {
        var output = RunModel("""
            using System.Numerics;

            [Validate]
            public class Amount<T> where T : INumber<T>
            {
                [GreaterThan(0, Message = "{PropertyName} was {PropertyValue}.")] public T Value { get; set; } = default!;
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains(
            "ErrorMessage = $\"Value was {(instance.Value is null ? \"null\" : System.Convert.ToString(instance.Value, System.Globalization.CultureInfo.InvariantCulture))}.\"",
            output.Source("Ns.AmountValidator`1.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void GenericValueObjectOnATypeParameter_UnwrapsToItsMember()
    {
        // A single-property value object closed over the model's type parameter reads its member,
        // of type T, and the type parameter policy applies to that member.
        var output = RunModel("""
            using System.Numerics;

            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly record struct Quantity<T>(T Value);

            [Validate]
            public class Line<T> where T : struct, INumber<T>
            {
                [GreaterThan(0)] public Quantity<T> Count { get; set; }
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains("if (double.CreateChecked(instance.Count.Value) <= 0)", output.Source("Ns.LineValidator`1.g.cs"), StringComparison.Ordinal);
    }


    // ---------------------------------------------------------------- behaviours

    private const string Behaviors = """
        using ZeroAlloc.Pipeline;

        public class Order { }

        [Validate] public class Page<T> { [NotEmpty] public string Title { get; set; } = ""; }
        [Validate] public class Pair<TKey, TValue> { [NotEmpty] public string Title { get; set; } = ""; }
        public class Outer<T>
        {
            [Validate] public class Inner<U> { [NotEmpty] public string Title { get; set; } = ""; }
            [Validate] public class Plain { [NotEmpty] public string Title { get; set; } = ""; }
        }
        [Validate] public class Flat { [NotEmpty] public string Title { get; set; } = ""; }

        """;

    private static string Behavior(string name, string appliesTo, int order) => $$"""
        [PipelineBehavior(Order = {{order}}, AppliesTo = typeof({{appliesTo}}))]
        public sealed class {{name}} : IPipelineBehavior
        {
            public static ValidationResult Handle<TModel>(TModel instance, Func<TModel, ValidationResult> next) => next(instance);
        }

        """;

    [Theory]
    [InlineData("Page<>", "Ns.PageValidator`1.g.cs", "Handle<global::Ns.Page<T>>")]
    [InlineData("Pair<,>", "Ns.PairValidator`2.g.cs", "Handle<global::Ns.Pair<TKey, TValue>>")]
    [InlineData("Outer<>.Inner<>", "Ns.Outer_InnerValidator`2.g.cs", "Handle<global::Ns.Outer<T>.Inner<U>>")]
    [InlineData("Outer<>.Plain", "Ns.Outer_PlainValidator`1.g.cs", "Handle<global::Ns.Outer<T>.Plain>")]
    public void BehaviorAppliedToTheOpenForm_RunsInTheGenericValidator(string appliesTo, string hintName, string call)
    {
        var output = RunModel(Behaviors + Behavior("Audit", appliesTo, 0)).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains("global::Ns.Audit." + call, output.Source(hintName), StringComparison.Ordinal);

        // It applies to that model only.
        foreach (var (hint, text) in output.Generated)
        {
            if (!string.Equals(hint, hintName, StringComparison.Ordinal))
                Assert.DoesNotContain("Audit", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BehaviorAppliedToAClosedForm_ReportsZV0038AndRunsNowhere()
    {
        var output = RunModel(Behaviors + Behavior("AuditOrders", "Page<Order>", 0) + Behavior("AuditInner", "Outer<int>.Plain", 1)).Compiles();

        Assert.Equal(["ZV0038", "ZV0038"], output.Ids());
        var pages = output.Diagnostics.First(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("AuditOrders", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Warning, pages.Severity);
        Assert.Equal("PipelineBehavior(Order = 0, AppliesTo = typeof(Page<Order>))", SourceAt(pages));
        Assert.Equal(
            "'AuditOrders' applies to 'Ns.Page<Ns.Order>', a closed form of the generic model 'Ns.Page<T>'; "
                + "a behaviour runs for every closing of a generic model, so name it as typeof(Page<>)",
            pages.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var plain = output.Diagnostics.First(d => !ReferenceEquals(d, pages));
        Assert.Equal(
            "'AuditInner' applies to 'Ns.Outer<int>.Plain', a closed form of the generic model 'Ns.Outer<T>.Plain'; "
                + "a behaviour runs for every closing of a generic model, so name it as typeof(Outer<>.Plain)",
            plain.GetMessage(System.Globalization.CultureInfo.InvariantCulture));

        Assert.DoesNotContain(output.Generated, g => g.Text.Contains("AuditOrders.Handle", StringComparison.Ordinal));
        Assert.DoesNotContain(output.Generated, g => g.Text.Contains("AuditInner.Handle", StringComparison.Ordinal));
    }

    [Fact]
    public void BehaviorAppliedToANonGenericModel_OrToAGenericTypeThatIsNotAModel_ReportsNothing()
    {
        var output = RunModel(Behaviors + Behavior("AuditFlat", "Flat", 0) + Behavior("AuditList", "List<Order>", 1)).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains("AuditFlat.Handle", output.Source("Ns.FlatValidator.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void BehaviorOnAGenericModelWithNestedValidators_UsesInstanceLambdas()
    {
        // The body reads the nested Line<T> validator through an instance field, so the chain's
        // lambdas cannot be static, #294; generic validators get the same treatment.
        var output = RunModel("""
            using ZeroAlloc.Pipeline;

            [Validate] public class Line<T> { [NotEmpty] public string Code { get; set; } = ""; }
            [Validate] public class Page<T> { public List<Line<T>> Lines { get; set; } = []; }

            [PipelineBehavior(AppliesTo = typeof(Page<>))]
            public sealed class Audit : IPipelineBehavior
            {
                public static ValidationResult Handle<TModel>(TModel instance, Func<TModel, ValidationResult> next) => next(instance);
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        var source = output.Source("Ns.PageValidator`1.g.cs");
        Assert.Contains("global::Ns.Audit.Handle<global::Ns.Page<T>>", source, StringComparison.Ordinal);
        Assert.Contains("_linesValidator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static r1", source, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- composition

    [Fact]
    public void ClosedGenericProperty_IsComposedAndItsClosingsRegistered()
    {
        var output = Run(PageSource).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains(
            "public OrderPageValidator(global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Page<global::Ns.Order>> pageValidator)",
            output.Source("Ns.OrderPageValidator.g.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public PageValidator(global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Line<TItem>> linesValidator)",
            output.Source("Ns.PageValidator`1.g.cs"),
            StringComparison.Ordinal);

        // The model first, then each closing it reaches, each followed by its registry entry.
        var registrations = output.Source(RegistrationHint);
        string[] expected =
        [
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.OrderPage>, global::Ns.OrderPageValidator>();",
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Page<global::Ns.Order>>, global::Ns.PageValidator<global::Ns.Order>>();",
            "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<global::ZeroAlloc.Validation.IModelValidator, global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Page<global::Ns.Order>>>(static sp => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Page<global::Ns.Order>>>(sp)));",
            "services.TryAddSingleton<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Line<global::Ns.Order>>, global::Ns.LineValidator<global::Ns.Order>>();",
            "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<global::ZeroAlloc.Validation.IModelValidator, global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Line<global::Ns.Order>>>(static sp => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Line<global::Ns.Order>>>(sp)));",
            "return services;",
        ];
        var lines = registrations.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("services.", StringComparison.Ordinal) || l.StartsWith("return", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(expected, lines);

        // The open model is never a root: nothing closed can be registered for it.
        Assert.DoesNotContain("TItem", registrations, StringComparison.Ordinal);
    }

    private const string RegistrationHint = "ZeroAlloc.Validation.ZeroAllocValidatorRegistrationExtensions.g.cs";

    private const string PageSource = """
        using System.Collections.Generic;
        using ZeroAlloc.Validation;
        namespace Ns;

        public class Order { }

        [Validate]
        public class Page<TItem> where TItem : class
        {
            [NotEmpty] public string Title { get; set; } = "";
            [NotNull]  public TItem? Selected { get; set; }
            public List<Line<TItem>> Lines { get; set; } = [];
        }

        [Validate]
        public class Line<TItem> where TItem : class
        {
            [GreaterThan(0)] public int Quantity { get; set; }
            [NotNull] public TItem? Item { get; set; }
        }

        [Validate]
        public class OrderPage
        {
            public Page<Order> Page { get; set; } = new();
        }
        """;

    [Fact]
    public void OnlyGenericModels_GetNoRegistrationMethod()
    {
        var output = RunModel("""
            [Validate] public class Page<T> { [NotEmpty] public string Title { get; set; } = ""; }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.DoesNotContain(output.Generated, g => string.Equals(g.HintName, RegistrationHint, StringComparison.Ordinal));
    }

    [Fact]
    public void ClosedGenericPropertyInEveryGlue_IsRegisteredClosed()
    {
        var output = Run(
            PageSource,
            new ValidatorGenerator(),
            new ZeroAlloc.Validation.Inject.InjectGenerator(),
            new ZeroAlloc.Validation.Options.Generator.OptionsValidationEmitter());

        output.Compiles();
        var options = output.Source("ZeroAlloc.Validation.ZeroAllocOptionsValidationExtensions.g.cs");
        Assert.Contains("this global::Microsoft.Extensions.Options.OptionsBuilder<global::Ns.OrderPage> builder)", options, StringComparison.Ordinal);
        Assert.Contains("global::Ns.PageValidator<global::Ns.Order>>();", options, StringComparison.Ordinal);
        Assert.Contains("global::Ns.LineValidator<global::Ns.Order>>();", options, StringComparison.Ordinal);
        Assert.Contains("TryAddEnumerable", options, StringComparison.Ordinal);
        // The generic model gets one overload over its type parameters, phase 2, never one per closing.
        Assert.Contains("OptionsBuilder<global::Ns.Page<TItem>> builder)", options, StringComparison.Ordinal);
        Assert.DoesNotContain("OptionsBuilder<global::Ns.Page<global::", options, StringComparison.Ordinal);

        // This host does not reference ASP.NET Core, so the filter glue is checked as text; it is
        // compiled and run in ZeroAlloc.Validation.Tests.AspNetCore.
        var aspNetCore = Run(PageSource, new ZeroAlloc.Validation.AspNetCore.Generator.AspNetCoreFilterEmitter());
        var extensions = aspNetCore.Source("ZeroAlloc.Validation.ZeroAllocValidationServiceCollectionExtensions.g.cs");
        Assert.Contains("global::Ns.PageValidator<global::Ns.Order>>();", extensions, StringComparison.Ordinal);
        Assert.Contains("TryAddEnumerable", extensions, StringComparison.Ordinal);
        var filter = aspNetCore.Source("ZeroAlloc.Validation.ZeroAllocValidationActionFilter.g.cs");
        Assert.Contains("case global::Ns.OrderPage orderPage_arg:", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("Page<", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void TypeParameterProperty_IsComposedOnlyThroughAValidateClassConstraint()
    {
        var output = RunModel("""
            [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
            public class Plain { }

            [Validate]
            public class Holder<TItem> where TItem : Address
            {
                public TItem? Item { get; set; }
                public List<TItem> Items { get; set; } = [];
            }

            [Validate]
            public class Chained<TItem, TInner> where TItem : TInner where TInner : Address
            {
                public TItem? Item { get; set; }
            }

            [Validate]
            public class Loose<TItem, TOther> where TOther : Plain
            {
                [NotEmpty] public string Name { get; set; } = "";
                public TItem? Item { get; set; }
                public List<TItem> Items { get; set; } = [];
                public TOther? Other { get; set; }
            }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        var holder = output.Source("Ns.HolderValidator`1.g.cs");
        Assert.Contains(
            "public HolderValidator(global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Address> itemValidator, global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Address> itemsValidator)",
            holder,
            StringComparison.Ordinal);
        Assert.Contains("var nestedResult = _itemValidator.Validate(instance.Item);", holder, StringComparison.Ordinal);
        Assert.Contains("_itemsValidator.Validate(_c0Item)", holder, StringComparison.Ordinal);
        Assert.Contains("ValidatorFor<global::Ns.Address> itemValidator)", output.Source("Ns.ChainedValidator`2.g.cs"), StringComparison.Ordinal);
        // Unconstrained, or constrained to a type without [Validate]: rules only, no constructor.
        Assert.DoesNotContain("public LooseValidator(", output.Source("Ns.LooseValidator`2.g.cs"), StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async System.Threading.Tasks.Task ExpandingNesting_ReportsZV0037AndIsNotComposed()
    {
        // Composing Next would need Node<Node<T>>'s validator, which needs Node<Node<Node<T>>>'s,
        // without end; a walk over it would never stop, hence the timeout.
        var output = await System.Threading.Tasks.Task.Run(() => RunModel("""
            [Validate]
            public class Node<T>
            {
                [NotEmpty] public string Name { get; set; } = "";
                public Node<Node<T>>? Next { get; set; }
                public List<Node<Node<T>>> Children { get; set; } = [];
                public Node<T>? Self { get; set; }
            }

            [Validate]
            public class Root
            {
                public Node<int> Tree { get; set; } = new();
            }
            """));

        output.Compiles();
        Assert.Equal(["ZV0037", "ZV0037"], output.Ids());
        var next = output.Diagnostics.First(d => string.Equals(SourceAt(d), "Next", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, next.Severity);
        Assert.Equal(
            "'Ns.Node<T>' nests 'Ns.Node<Ns.Node<T>>?' inside itself, so its validators would form an unbounded chain; "
                + "validate it with [ValidateWith] or a [Must] rule",
            next.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(output.Diagnostics, d => string.Equals(SourceAt(d), "Children", StringComparison.Ordinal));

        // Plain self-reference is composed, as for a non-generic model.
        Assert.Contains(
            "public NodeValidator(global::ZeroAlloc.Validation.ValidatorFor<global::Ns.Node<T>> selfValidator)",
            output.Source("Ns.NodeValidator`1.g.cs"),
            StringComparison.Ordinal);
        var registrations = output.Source(RegistrationHint);
        Assert.Contains("ValidatorFor<global::Ns.Node<int>>, global::Ns.NodeValidator<int>>();", registrations, StringComparison.Ordinal);
        Assert.DoesNotContain("Node<global::Ns.Node<int>>", registrations, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async System.Threading.Tasks.Task ExpandingNestingThroughAnotherModel_ReportsZV0037AtTheGrowingProperty()
    {
        // A<T> holds B<List<T>>, and B<U> holds A<U>: every round wraps the argument in one more
        // List. The property whose argument grows is the one left out.
        var output = await System.Threading.Tasks.Task.Run(() => RunModel("""
            [Validate] public class A<T> { [NotEmpty] public string Name { get; set; } = ""; public B<List<T>>? Down { get; set; } }
            [Validate] public class B<U> { [NotEmpty] public string Name { get; set; } = ""; public A<U>? Up { get; set; } }
            [Validate] public class Root { public A<int> Start { get; set; } = new(); }
            """));

        output.Compiles();
        var zv0037 = output.OnlyDiagnostic();
        Assert.Equal("ZV0037", zv0037.Id);
        Assert.Equal("Down", SourceAt(zv0037));
        Assert.DoesNotContain("_downValidator", output.Source("Ns.AValidator`1.g.cs"), StringComparison.Ordinal);
        Assert.Contains("_upValidator", output.Source("Ns.BValidator`1.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void FiniteGenericCycles_AreComposed()
    {
        // Reordered or replaced type arguments reach finitely many closings.
        var output = RunModel("""
            [Validate]
            public class Pair<T, U>
            {
                [NotEmpty] public string Name { get; set; } = "";
                public Pair<U, T>? Swapped { get; set; }
                public Pair<string, string>? Meta { get; set; }
            }

            [Validate] public class Root { public Pair<int, long> Start { get; set; } = new(); }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        var registrations = output.Source(RegistrationHint);
        Assert.Contains("ValidatorFor<global::Ns.Pair<int, long>>, global::Ns.PairValidator<int, long>>();", registrations, StringComparison.Ordinal);
        Assert.Contains("ValidatorFor<global::Ns.Pair<long, int>>, global::Ns.PairValidator<long, int>>();", registrations, StringComparison.Ordinal);
        Assert.Contains("ValidatorFor<global::Ns.Pair<string, string>>, global::Ns.PairValidator<string, string>>();", registrations, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateWithOnAClosedGenericProperty_IsReportedAsRedundant()
    {
        // Box<int> now gets the generated BoxValidator<int>, so [ValidateWith] is redundant.
        var output = RunModel("""
            [Validate] public class Box<T> { [NotEmpty] public string Name { get; set; } = ""; }

            public class BoxOfIntValidator : ValidatorFor<Box<int>>
            {
                public override ValidationResult Validate(Box<int> instance) => new([]);
            }

            [Validate]
            public class Order
            {
                [ValidateWith(typeof(BoxOfIntValidator))]
                public Box<int> Item { get; set; } = new();
            }
            """).Compiles();

        Assert.Equal(["ZV0011"], output.Ids());
        Assert.Contains("global::Ns.BoxOfIntValidator itemValidator", output.Source("Ns.OrderValidator.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatorTakingAClosingOfAnInternalType_IsInternal()
    {
        var output = RunModel("""
            internal class Secret { }

            [Validate] public class Page<T> { [NotEmpty] public string Title { get; set; } = ""; public List<Line<T>> Lines { get; set; } = []; }
            [Validate] public class Line<T> { [NotEmpty] public string Code { get; set; } = ""; }
            [Validate] public class OrderPage { internal Page<Secret> Page { get; set; } = new(); }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        Assert.Contains("public sealed partial class PageValidator<T>", output.Source("Ns.PageValidator`1.g.cs"), StringComparison.Ordinal);
        Assert.Contains("internal sealed partial class OrderPageValidator", output.Source("Ns.OrderPageValidator.g.cs"), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- asynchronous rules

    [Fact]
    public void AsynchronousRuleOnAGenericModel_MakesItAndItsComposersAsynchronous()
    {
        var output = RunModel("""
            using System.Threading;
            using System.Threading.Tasks;

            [AttributeUsage(AttributeTargets.Property)]
            public sealed class KnownAttribute : AsyncValidationAttribute<object?>
            {
                public override ValueTask<bool> IsValidAsync(object? value, CancellationToken ct) => new(value is not null);
            }

            [Validate] public class Box<T> { [Known] public T Value { get; set; } = default!; }
            [Validate] public class Holder { public Box<int> Box { get; set; } = new(); public List<Box<string>> Boxes { get; set; } = []; }
            """).Compiles();

        Assert.Empty(output.Diagnostics);
        var box = output.Source("Ns.BoxValidator`1.g.cs");
        Assert.Contains("private static async global::System.Threading.Tasks.ValueTask<global::ZeroAlloc.Validation.ValidationResult> __ValidateAsyncCore(global::Ns.Box<T> instance", box, StringComparison.Ordinal);
        Assert.Contains("await __Rule_Value_0.IsValidAsync(instance.Value, ct)", box, StringComparison.Ordinal);
        var holder = output.Source("Ns.HolderValidator.g.cs");
        Assert.Contains("await _boxValidator.ValidateAsync(instance.Box, ct)", holder, StringComparison.Ordinal);
        Assert.Contains("throw new global::System.NotSupportedException", holder, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- probes

    [Fact]
    public void MethodTheGenericValidatorCannotCall_IsReportedOnce()
    {
        var output = RunModel("""
            [Validate]
            public class Box<T>
            {
                [Must(nameof(IsKnown))] public T Value { get; set; } = default!;
                [Must(nameof(IsCode))] public T Code { get; set; } = default!;

                public static bool IsKnown(T value) => value is not null;
                public bool IsCode(int value) => value > 0;
            }

            [Validate] public class Root { public Box<int> Box { get; set; } = new(); }
            """).Compiles();

        Assert.Equal(["ZV0028", "ZV0030"], output.Ids());
        Assert.Equal("Must(nameof(IsKnown))", SourceAt(output.Diagnostics.First(d => string.Equals(d.Id, "ZV0028", StringComparison.Ordinal))));
        Assert.Equal("Must(nameof(IsCode))", SourceAt(output.Diagnostics.First(d => string.Equals(d.Id, "ZV0030", StringComparison.Ordinal))));
    }

    [Fact]
    public void CompilerWarningOnAGenericValidatorsCall_IsMirroredOnceAndSuppressed()
    {
        var output = RunModel("""
            [Validate]
            public class Box<T>
            {
                [Must(nameof(IsKnown))] public T Value { get; set; } = default!;

                [Obsolete]
                public bool IsKnown(T value) => value is not null;
            }
            """).Compiles();

        var zv0032 = output.OnlyDiagnostic();
        Assert.Equal("ZV0032", zv0032.Id);
        Assert.Contains("CS0612", zv0032.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal("Must(nameof(IsKnown))", SourceAt(zv0032));
        Assert.Contains("#pragma warning disable CS0612", output.Source("Ns.BoxValidator`1.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ObsoleteErrorPropertyOfAGenericModel_IsReportedOnceAndNotRead()
    {
        var output = RunModel("""
            [Validate]
            public class Box<T>
            {
                [Obsolete("gone", error: true)]
                [NotNull] public T? Old { get; set; }
            }
            """).Compiles();

        var zv0032 = output.OnlyDiagnostic();
        Assert.Equal("ZV0032", zv0032.Id);
        Assert.Equal(DiagnosticSeverity.Error, zv0032.Severity);
        Assert.Contains("CS0619", zv0032.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain("instance.Old", output.Source("Ns.BoxValidator`1.g.cs"), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- base types

    [Fact]
    public void MembersOfAGenericValidateBase_AreReportedOnce_ByItsOwnValidator()
    {
        // Page<T> gets a validator now and reports its members itself; OrderPage : Page<Order>,
        // also [Validate], leaves them to it.
        var output = RunModel("""
            public class Order { }

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }

            [Validate]
            public class Page<T>
            {
                [NotBlank] public string? Code;

                [Must(nameof(IsKnown))] public string Name { get; set; } = "";

                [NotEmpty] protected string? Secret { get; set; }

                [NotEmpty] public static string? Shared { get; set; }

                public static bool IsKnown(string value) => value.Length > 0;
            }

            [Validate]
            public class OrderPage : Page<Order> { [NotEmpty] public string Extra { get; set; } = ""; }
            """).Compiles();

        // ZV0024 for the field Code, ZV0028 for the static IsKnown, and ZV0027 for the protected
        // Secret and the static Shared, each once.
        Assert.Equal(["ZV0024", "ZV0027", "ZV0027", "ZV0028"], output.Ids());
        Assert.Contains("instance.Extra", output.Source("Ns.OrderPageValidator.g.cs"), StringComparison.Ordinal);
    }
}
