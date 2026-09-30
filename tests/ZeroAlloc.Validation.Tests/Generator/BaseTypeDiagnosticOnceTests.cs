using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// A usage on a base type is walked by every derived <c>[Validate]</c> model, and by the base
/// type's own validator when it has one. Each diagnostic about the usage is reported once, and a
/// usage on the model itself is still reported. #283 and #284 did this for a <c>[Validate]</c>
/// base type, which reported the usage itself; #290 for a plain base type, whose usages the
/// step of the declaring type reports, however many models derive from it.
/// </summary>
public class BaseTypeDiagnosticOnceTests
{
    private const string ValueObjectStub = """
        namespace ZeroAlloc.ValueObjects
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ValueObjectAttribute : System.Attribute { }
        }
        """;

    /// <summary>The declarations a case needs, and the property on the base type that trips it.</summary>
    private static readonly Dictionary<string, (string Declarations, string Property)> Cases = new(StringComparer.Ordinal)
    {
        // A built-in rule on a multi-property value object.
        ["ZV0016"] = ("""
            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly struct Money
            {
                public decimal Amount { get; }
                public string Currency { get; }
            }
            """,
            "[NotEmpty] public Money Total { get; set; }"),

        // An unknown placeholder in a custom rule's message.
        ["ZV0022"] = ("""
            public sealed class TaggedAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => value is not null;
            }
            """,
            "[Tagged(Message = \"{nope}\")] public string? Name { get; set; }"),

        // [ValidateWith] on a type that already gets a generated validator.
        ["ZV0011"] = ("""
            [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
            """,
            "[ValidateWith(typeof(AddressValidator))] public Address Home { get; set; } = new();"),

        // [ValidateWith] naming a validator for another type.
        ["ZV0012"] = ("""
            [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
            [Validate] public class Name { [NotEmpty] public string Value { get; set; } = ""; }
            """,
            "[ValidateWith(typeof(NameValidator))] public Address Home { get; set; } = new();"),

        // The same rule twice with identical arguments.
        ["ZV0018"] = ("", "[NotEmpty] [NotEmpty] public string? Tenant { get; set; }"),

        // A ValidationAttribute subclass that is not a rule the generator can emit.
        ["ZV0020"] = ("""
            public sealed class NotBlankAttribute : ValidationAttribute { }
            """,
            "[NotBlank] public string? Code { get; set; }"),

        // A custom rule whose T the property type does not convert to.
        ["ZV0021"] = ("""
            public sealed class PositiveAttribute : ValidationAttribute<int>
            {
                public override bool IsValid(int value) => value > 0;
            }
            """,
            "[Positive] public string? Code { get; set; }"),

        // A custom rule the generated validator cannot reach.
        ["ZV0023"] = ("", """
            protected sealed class HiddenAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => value is not null;
            }
            [Hidden] public string? Code { get; set; }
            """),

        // A rule on a field.
        ["ZV0024"] = ("""
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
            public sealed class NotBlankAttribute : ValidationAttribute<string?>
            {
                public override bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
            }
            """,
            "[NotBlank] public string? Code;"),

        // A rule on a property the generated validator cannot read.
        ["ZV0027"] = ("", "[NotEmpty] public static string? Code { get; set; }"),

        // A numeric comparison on a type that is not a number.
        ["ZV0033"] = ("", "[GreaterThan(0)] public System.DateTime Due { get; set; }"),

        // The ones that depend on the model: the method a rule calls, and the call itself.

        // A [CustomValidation] method with a signature the validator cannot use.
        ["ZV0013"] = ("", "[CustomValidation] public int Check() => 0;"),

        // A [Must] predicate the generated validator cannot call.
        ["ZV0028"] = ("", """
            [Must(nameof(Check))] public string? Code { get; set; }
            public static bool Check(string? value) => true;
            """),

        // A [Must] predicate whose call does not compile: its result is not a condition. An
        // argument error is not reported on a partial type, which the partial case uses.
        ["ZV0030"] = ("", """
            [Must(nameof(Check))] public string? Code { get; set; }
            public string Check(string? value) => "";
            """),

        // A [Must] predicate whose call raises a compiler warning.
        ["ZV0032"] = ("", """
            [Must(nameof(Check))] public string? Code { get; set; }
            public bool Check(string value) => true;
            """),
    };

    public static TheoryData<string> Ids() => new(Cases.Keys);

    // A generic type cannot contain an attribute class, CS0698, so ZV0023 has no generic case.
    // ZV0032 carries the compiler's message, which names the method's constructed type, such as
    // Entity<int>.Check, so each construction's is its own.
    public static TheoryData<string> GenericIds() => new(Cases.Keys.Where(id => id is not ("ZV0023" or "ZV0032")));

    [Theory]
    [MemberData(nameof(Ids))]
    public void OnAValidatedBaseType_WithSeveralDerivedModels_IsReportedOnce(string id)
    {
        var (declarations, property) = Cases[id];
        var result = Run($$"""
            {{declarations}}
            [Validate]
            public class Booking
            {
                {{property}}
            }
            [Validate] public class GroupBooking : Booking { }
            [Validate] public class CorporateBooking : Booking { }
            [Validate] public class VipGroupBooking : GroupBooking { }
            """);

        Assert.Equal(1, Count(result, id));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void OnTheModelItself_IsReported(string id)
    {
        var (declarations, property) = Cases[id];
        var result = Run($$"""
            {{declarations}}
            [Validate]
            public class Booking
            {
                {{property}}
            }
            """);

        Assert.Equal(1, Count(result, id));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void OnAPlainBaseType_IsReportedByTheModel(string id)
    {
        var (declarations, property) = Cases[id];
        var result = Run($$"""
            {{declarations}}
            public class Booking
            {
                {{property}}
            }
            [Validate] public class GroupBooking : Booking { }
            """);

        Assert.Equal(1, Count(result, id));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void OnAPlainBaseType_WithSeveralDerivedModelsOnSeveralLevels_IsReportedOnce(string id)
    {
        // No model can tell whether another reports the usage, so the step of the type declaring
        // it reports it, issue #290.
        var (declarations, property) = Cases[id];
        var result = Run($$"""
            {{declarations}}
            public class Entity
            {
                {{property}}
            }
            public class Booking : Entity { }
            [Validate] public class GroupBooking : Booking { }
            [Validate] public class CorporateBooking : Booking { }
            [Validate] public class VipGroupBooking : GroupBooking { }
            [Validate] public class Invoice : Entity { }
            """);

        Assert.Equal(1, Count(result, id));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void OnAPartialPlainBaseType_AcrossFiles_IsReportedOnce(string id)
    {
        // The usage is in the second part of the base type, and the models in three files.
        var (declarations, property) = Cases[id];
        var result = Run(
            $$"""
            {{declarations}}
            public partial class Booking
            {
                public string? Note { get; set; }
            }
            [Validate] public class GroupBooking : Booking { }
            """,
            $$"""
            public partial class Booking
            {
                {{property}}
            }
            [Validate] public class CorporateBooking : Booking { }
            """,
            """
            [Validate] public class VipBooking : Booking { }
            """);

        Assert.Equal(1, Count(result, id));
    }

    [Theory]
    [MemberData(nameof(GenericIds))]
    public void OnAGenericPlainBaseType_WithSeveralConstructions_IsReportedOnce(string id)
    {
        // Each construction is checked, and a diagnostic that does not depend on the type
        // argument is the same for each, so it is reported once.
        var (declarations, property) = Cases[id];
        var result = Run($$"""
            {{declarations}}
            public class Entity<TKey>
            {
                public TKey? Key { get; set; }
                {{property}}
            }
            [Validate] public class GroupBooking : Entity<int> { }
            [Validate] public class CorporateBooking : Entity<int> { }
            [Validate] public class Invoice : Entity<string> { }
            """);

        Assert.Equal(1, Count(result, id));
    }

    [Fact]
    public void OnAGenericPlainBaseType_ADiagnosticThatDependsOnTheTypeArgument_IsReportedPerConstruction()
    {
        // string converts to string?, int and long do not, and each message names its own type.
        var (declarations, _) = Cases["ZV0022"];
        var result = Run($$"""
            {{declarations}}
            public class Entity<TKey>
            {
                [Tagged] public TKey Key { get; set; } = default!;
            }
            [Validate] public class GroupBooking : Entity<int> { }
            [Validate] public class CorporateBooking : Entity<int> { }
            [Validate] public class Invoice : Entity<long> { }
            [Validate] public class Receipt : Entity<string> { }
            """);

        var messages = result.Diagnostics
            .Where(d => string.Equals(d.Id, "ZV0021", StringComparison.Ordinal))
            .Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, m => m.Contains("'int'", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("'long'", StringComparison.Ordinal));
    }

    [Fact]
    public void OnAPlainBaseType_HiddenByOneDerivedModel_IsReportedForTheOther()
    {
        var (declarations, property) = Cases["ZV0022"];
        var result = Run($$"""
            {{declarations}}
            public class Booking
            {
                {{property}}
            }
            [Validate] public class GroupBooking : Booking { public new string? Name { get; set; } }
            [Validate] public class CorporateBooking : Booking { }
            [Validate] public class VipBooking : Booking { }
            """);

        Assert.Equal(1, Count(result, "ZV0022"));
    }

    [Fact]
    public void OnAPlainBaseType_HiddenByEveryDerivedModel_IsNotReported()
    {
        // No validator reads the rule, so nothing is wrong with what it emits.
        var (declarations, property) = Cases["ZV0022"];
        var result = Run($$"""
            {{declarations}}
            public class Booking
            {
                {{property}}
            }
            [Validate] public class GroupBooking : Booking { public new string? Name { get; set; } }
            [Validate] public class CorporateBooking : Booking { public new string? Name { get; set; } }
            """);

        Assert.Equal(0, Count(result, "ZV0022"));
    }

    [Fact]
    public void OnAPlainBaseType_WhoseOnlyDerivedModelGetsNoValidator_IsNotReported()
    {
        // A model that gets no validator, ZV0025 here, walks nothing, as before.
        var (declarations, property) = Cases["ZV0022"];
        var result = Run($$"""
            {{declarations}}
            public class Booking
            {
                {{property}}
            }
            public class Outer
            {
                [Validate] private class GroupBooking : Booking { }
            }
            """);

        Assert.Equal(1, Count(result, "ZV0025"));
        Assert.Equal(0, Count(result, "ZV0022"));
    }

    [Fact]
    public void CallDiagnostic_ThatNamesTheModel_IsReportedForEachModel()
    {
        // ZV0017 names the model whose validator leaves the rule out, so each model's is its own.
        var result = Run("""
            public class Booking
            {
                [Must(nameof(Check))] public string? Code { get; set; }
                private bool Check(string? value) => true;
            }
            [Validate] public class GroupBooking : Booking { }
            [Validate] public class CorporateBooking : Booking { }
            """);

        Assert.Equal(2, Count(result, "ZV0017"));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void AboveABaseTypeThatDoesNotIncludeBaseProperties_IsReportedByTheModel(string id)
    {
        // The [Validate] base type does not walk the type above it, so the model reports the usage.
        var (declarations, property) = Cases[id];
        var result = Run($$"""
            {{declarations}}
            public class Entity
            {
                {{property}}
            }
            [Validate(IncludeBaseProperties = false)] public class Booking : Entity { }
            [Validate] public class GroupBooking : Booking { }
            """);

        Assert.Equal(1, Count(result, id));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void OnAModelWithPipelineBehaviors_IsReportedOnce(string id)
    {
        // The rules are emitted inside the behavior chain; the chain does not repeat them.
        var (declarations, property) = Cases[id];
        var result = Run($$"""
            {{declarations}}
            [ZeroAlloc.Pipeline.PipelineBehavior(Order = 0)]
            public class First : ZeroAlloc.Pipeline.IPipelineBehavior
            {
                public static ValidationResult Handle<TModel>(TModel instance, System.Func<TModel, ValidationResult> next) => next(instance);
            }
            [ZeroAlloc.Pipeline.PipelineBehavior(Order = 1)]
            public class Second : ZeroAlloc.Pipeline.IPipelineBehavior
            {
                public static ValidationResult Handle<TModel>(TModel instance, System.Func<TModel, ValidationResult> next) => next(instance);
            }
            [Validate]
            public class Booking
            {
                {{property}}
            }
            """);

        Assert.Equal(1, Count(result, id));
    }

    [Fact]
    public void UnknownPlaceholder_OnARuleWhoseWhenMethodOnlyTheDerivedModelCanCall_IsReportedOnce()
    {
        // Whether a rule's When method can be called depends on the model: the base type's returns
        // a string, ZV0030, and the derived model hides it with one that returns bool, so only the
        // derived validator emits the rule. ZV0022 depends on the usage alone, so the base type's
        // validator still reports it, once.
        var (declarations, _) = Cases["ZV0022"];
        var result = Run($$"""
            {{declarations}}
            [Validate]
            public class Booking
            {
                [Tagged(Message = "{nope}", When = "IsGroup")] public string? Name { get; set; }
                public string IsGroup() => "";
            }
            [Validate]
            public class GroupBooking : Booking
            {
                public new bool IsGroup() => true;
            }
            """);

        Assert.Equal(1, Count(result, "ZV0022"));
        Assert.Equal(1, Count(result, "ZV0030"));
        Assert.Contains(
            "{nope}",
            GeneratorTestHelper.GetGeneratedSource(result, "TestModels.GroupBookingValidator.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownPlaceholder_OnARuleLeftOutForAWhenMethodThatDoesNotCompile_IsStillReported()
    {
        var (declarations, _) = Cases["ZV0022"];
        var result = Run($$"""
            {{declarations}}
            [Validate]
            public class Booking
            {
                [Tagged(Message = "{nope}", When = "IsGroup")] public string? Name { get; set; }
                public string IsGroup() => "";
            }
            """);

        Assert.Equal(1, Count(result, "ZV0022"));
        Assert.Equal(1, Count(result, "ZV0030"));
    }

    private static int Count(GeneratorDriverRunResult result, string id) =>
        result.Diagnostics.Count(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    private const string Preamble = "using ZeroAlloc.Validation;\nnamespace TestModels;\n";

    private static GeneratorDriverRunResult Run(string body, params string[] otherFiles) =>
        GeneratorTestHelper.RunGenerator(
            Preamble + body,
            extraSources: [ValueObjectStub, .. otherFiles.Select(file => Preamble + file)],
            extraReferences: [MetadataReference.CreateFromFile(typeof(ZeroAlloc.Pipeline.IPipelineBehavior).Assembly.Location)],
            nullableContextOptions: NullableContextOptions.Enable);
}
