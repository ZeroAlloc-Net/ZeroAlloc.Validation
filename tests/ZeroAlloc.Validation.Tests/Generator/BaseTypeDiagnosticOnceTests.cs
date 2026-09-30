using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Generator;

/// <summary>
/// A usage on a <c>[Validate]</c> base type is checked by the base type's validator and again by
/// every derived <c>[Validate]</c> model that walks its properties. Each diagnostic about the
/// usage is reported once, by the base type's validator, and a usage on the model itself is still
/// reported. #283 did this for ZV0020, ZV0021, ZV0023 and ZV0033; #284 for the rest.
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
    };

    public static TheoryData<string> Ids() => new(Cases.Keys);

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

    private static GeneratorDriverRunResult Run(string body) =>
        GeneratorTestHelper.RunGenerator(
            "using ZeroAlloc.Validation;\nnamespace TestModels;\n" + body,
            extraSources: [ValueObjectStub],
            extraReferences: [MetadataReference.CreateFromFile(typeof(ZeroAlloc.Pipeline.IPipelineBehavior).Assembly.Location)],
            nullableContextOptions: NullableContextOptions.Enable);
}
