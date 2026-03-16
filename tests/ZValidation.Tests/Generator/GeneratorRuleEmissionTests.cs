using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZValidation;
using ZValidation.Generator;

namespace ZValidation.Tests.Generator;

public class GeneratorRuleEmissionTests
{
    [Fact]
    public void Generator_EmitsNotEmpty_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("IsNullOrEmpty", generated, StringComparison.Ordinal);
        Assert.Contains("\"Name\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsMaxLength_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person { [MaxLength(50)] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains(".Length > 50", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsGreaterThan_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person { [GreaterThan(0)] public int Age { get; set; } }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("<= 0", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_DefaultContinueMode_EmitsSeparateIf_NotElseIf()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person
            {
                [NotEmpty]
                [MaxLength(50)]
                public string Name { get; set; } = "";
            }
            """;

        var generated = RunGeneratorGetSource(source);
        // In continue mode, rules are independent ifs — no else if
        Assert.DoesNotContain("else if", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_StopOnFirstFailure_EmitsElseIf()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person
            {
                [StopOnFirstFailure]
                [NotEmpty]
                [MaxLength(50)]
                public string Name { get; set; } = "";
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("else if", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_StopOnFirstFailure_OnlyAffectsTaggedProperty()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person
            {
                [StopOnFirstFailure]
                [NotEmpty]
                [MaxLength(50)]
                public string Name { get; set; } = "";

                [GreaterThan(0)]
                [LessThan(120)]
                public int Age { get; set; }
            }
            """;

        var generated = RunGeneratorGetSource(source);
        // Name has else if (stop mode), but Age rules use separate if (continue mode)
        // The generated code has exactly one "else if" (for Name's second rule)
        var elseIfCount = CountOccurrences(generated, "else if");
        Assert.Equal(1, elseIfCount);
    }

    [Fact]
    public void Generator_EmitsStackalloc_SizedToRuleCount()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Person
            {
                [NotEmpty]
                [MaxLength(50)]
                public string Name { get; set; } = "";
                [GreaterThan(0)]
                public int Age { get; set; }
            }
            """;

        // 3 rules total
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("ValidationFailure[3]", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_UsesFailureBufferForModelWithNestedValidateType()
    {
        var source = """
            using ZValidation;
            namespace TestModels;

            [Validate]
            public class Address
            {
                [NotEmpty]
                public string Street { get; set; } = "";
            }

            [Validate]
            public class Customer
            {
                [NotEmpty]
                public string Name { get; set; } = "";
                public Address Address { get; set; } = new();
            }
            """;

        var customerSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("CustomerValidator", StringComparison.Ordinal));

        Assert.Contains("FailureBuffer", customerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FailureBuffer", RunGeneratorGetSources(source)
            .First(s => s.Contains("AddressValidator", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsNestedValidation_WithDotPrefix()
    {
        var source = """
            using ZValidation;
            namespace TestModels;

            [Validate]
            public class Address
            {
                [NotEmpty]
                public string Street { get; set; } = "";
            }

            [Validate]
            public class Customer
            {
                public Address Address { get; set; } = new();
            }
            """;

        var customerSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("CustomerValidator", StringComparison.Ordinal));

        Assert.Contains("AddressValidator", customerSource, StringComparison.Ordinal);
        Assert.Contains("\"Address.\" +", customerSource, StringComparison.Ordinal);
        Assert.Contains("is not null", customerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsFullyQualifiedValidatorName_ForCrossNamespaceNestedType()
    {
        var source = """
            namespace Models.Addresses
            {
                [ZValidation.Validate]
                public class Address { [ZValidation.NotEmpty] public string Street { get; set; } = ""; }
            }
            namespace Models.Orders
            {
                [ZValidation.Validate]
                public class Order { public Models.Addresses.Address Shipping { get; set; } = new(); }
            }
            """;

        var orderSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("OrderValidator", StringComparison.Ordinal));

        Assert.Contains("global::Models.Addresses.AddressValidator", orderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsNullGuard_ForNullableNestedProperty()
    {
        // The generated code must have the null guard
        var source = """
            using ZValidation;
            namespace TestModels;

            [Validate]
            public class Address { [NotEmpty] public string Street { get; set; } = ""; }

            [Validate]
            public class Customer { public Address? Address { get; set; } }
            """;

        var customerSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("CustomerValidator", StringComparison.Ordinal));

        Assert.Contains("is not null", customerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_UsesFailureBufferForModelWithCollectionOfValidateType()
    {
        var source = """
            using ZValidation;
            using System.Collections.Generic;
            namespace TestModels;

            [Validate]
            public class LineItem
            {
                [NotEmpty]
                public string Sku { get; set; } = "";
            }

            [Validate]
            public class Order
            {
                [NotEmpty]
                public string Reference { get; set; } = "";
                public List<LineItem> LineItems { get; set; } = new();
            }
            """;

        var orderSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("OrderValidator", StringComparison.Ordinal));

        Assert.Contains("FailureBuffer", orderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FailureBuffer", RunGeneratorGetSources(source)
            .First(s => s.Contains("LineItemValidator", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsCollectionValidation_WithBracketIndex()
    {
        var source = """
            using ZValidation;
            using System.Collections.Generic;
            namespace TestModels;

            [Validate]
            public class LineItem { [NotEmpty] public string Sku { get; set; } = ""; }

            [Validate]
            public class Order { public List<LineItem> Items { get; set; } = new(); }
            """;

        var orderSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("OrderValidator", StringComparison.Ordinal));

        Assert.Contains("LineItemValidator", orderSource, StringComparison.Ordinal);
        Assert.Contains("\"Items[\" +", orderSource, StringComparison.Ordinal);
        Assert.Contains("is not null", orderSource, StringComparison.Ordinal);
        Assert.Contains("foreach", orderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_DetectsArrayOfValidateType()
    {
        var source = """
            using ZValidation;
            namespace TestModels;

            [Validate]
            public class Tag { [NotEmpty] public string Name { get; set; } = ""; }

            [Validate]
            public class Article { public Tag[] Tags { get; set; } = []; }
            """;

        var articleSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("ArticleValidator", StringComparison.Ordinal));

        Assert.Contains("TagValidator", articleSource, StringComparison.Ordinal);
        Assert.Contains("FailureBuffer", articleSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsNullGuard_ForCollectionProperty()
    {
        var source = """
            using ZValidation;
            using System.Collections.Generic;
            namespace TestModels;

            [Validate]
            public class Item { [NotEmpty] public string Name { get; set; } = ""; }

            [Validate]
            public class Bag { public List<Item>? Items { get; set; } }
            """;

        var bagSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("BagValidator", StringComparison.Ordinal));

        Assert.Contains("is not null", bagSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsNull_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [Null] public string? Name { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("is not null", generated, StringComparison.Ordinal);
        Assert.Contains("\"Name\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsEmpty_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [Empty] public string? Name { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("IsNullOrEmpty", generated, StringComparison.Ordinal);
        Assert.Contains("\"Name\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsEqual_Numeric_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [Equal(42.0)] public int Value { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("!= 42", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsEqual_String_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [Equal("active")] public string Status { get; set; } = ""; }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("!= \"active\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsNotEqual_Numeric_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [NotEqual(0.0)] public double Score { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("== 0", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsNotEqual_String_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [NotEqual("inactive")] public string Status { get; set; } = ""; }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("== \"inactive\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsGreaterThanOrEqualTo_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [GreaterThanOrEqualTo(0)] public int Age { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("< 0", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsLessThanOrEqualTo_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [LessThanOrEqualTo(100)] public int Score { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("> 100", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsExclusiveBetween_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [ExclusiveBetween(0, 100)] public int Value { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("<= 0", generated, StringComparison.Ordinal);
        Assert.Contains(">= 100", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsLength_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [Length(2, 50)] public string Name { get; set; } = ""; }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains(".Length < 2", generated, StringComparison.Ordinal);
        Assert.Contains(".Length > 50", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsIsInEnum_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            public enum Color { Red, Green, Blue }
            [Validate]
            public class Foo { [IsInEnum] public Color Hue { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("IsDefined", generated, StringComparison.Ordinal);
        Assert.Contains("Color", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsPrecisionScale_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [PrecisionScale(5, 2)] public decimal Amount { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("ExceedsPrecisionScale", generated, StringComparison.Ordinal);
        Assert.Contains("5", generated, StringComparison.Ordinal);
        Assert.Contains("2", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsIsEnumName_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            public enum Color { Red, Green, Blue }
            [Validate]
            public class Foo { [IsEnumName(typeof(Color))] public string ColorName { get; set; } = ""; }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("IsDefined", generated, StringComparison.Ordinal);
        Assert.Contains("Color", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsMust_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Widget
            {
                [Must(nameof(IsValidCode))]
                public string Code { get; set; } = "";
                public bool IsValidCode(string value) => value.StartsWith("W", System.StringComparison.Ordinal);
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("!instance.IsValidCode(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsMust_DefaultMessage()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Widget
            {
                [Must(nameof(IsValidCode))]
                public string Code { get; set; } = "";
                public bool IsValidCode(string value) => value.StartsWith("W", System.StringComparison.Ordinal);
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("Code is invalid.", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsWhen_Guard()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Order
            {
                public bool NeedsShipping { get; set; }
                [NotNull(When = nameof(IsShippingRequired))]
                public string? ShippingAddress { get; set; }
                public bool IsShippingRequired() => NeedsShipping;
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("instance.IsShippingRequired() &&", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsUnless_Guard()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Profile
            {
                public bool IsGuest { get; set; }
                [NotEmpty(Unless = nameof(AllowEmpty))]
                public string Name { get; set; } = "";
                public bool AllowEmpty() => IsGuest;
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("!instance.AllowEmpty() &&", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsBothWhenAndUnless_Guards()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Doc
            {
                public bool IsPublished { get; set; }
                public bool AllowShortTitle { get; set; }
                [MinLength(10, When = nameof(CheckTitle), Unless = nameof(ShortTitleOk))]
                public string Title { get; set; } = "";
                public bool CheckTitle() => IsPublished;
                public bool ShortTitleOk() => AllowShortTitle;
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("instance.CheckTitle() &&", generated, StringComparison.Ordinal);
        Assert.Contains("!instance.ShortTitleOk() &&", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_Placeholder_PropertyName_Replaced()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [NotEmpty(Message = "'{PropertyName}' is required")] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        // {PropertyName} replaced with "Name", so the emitted string literal is "'Name' is required"
        Assert.Contains("'Name' is required", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{PropertyName}", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_Placeholder_ComparisonValue_Replaced()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [GreaterThan(18, Message = "Must be > {ComparisonValue}")] public int Age { get; set; } }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("Must be > 18", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{ComparisonValue}", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_Placeholder_FromTo_Replaced()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [ExclusiveBetween(0, 100, Message = "Between {From} and {To}")] public double Value { get; set; } }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("Between 0 and 100", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{From}", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{To}", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_Placeholder_MinMaxLength_Replaced()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [Length(2, 50, Message = "Length {MinLength}\u201350")] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("Length 2\u201350", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{MinLength}", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{MaxLength}", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_Placeholder_MinLength_Replaced()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [MinLength(3, Message = "Min {MinLength} chars")] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("Min 3 chars", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{MinLength}", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_Placeholder_MaxLength_Replaced()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [MaxLength(50, Message = "Max {MaxLength} chars")] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("Max 50 chars", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{MaxLength}", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_Placeholder_InclusiveBetween_FromTo_Replaced()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [InclusiveBetween(1, 10, Message = "Between {From} and {To}")] public double Value { get; set; } }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("Between 1 and 10", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{From}", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("{To}", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsMust_WithWhen_Guard()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Widget
            {
                public bool IsEnabled { get; set; }
                [Must(nameof(IsValidCode), When = nameof(EnabledCheck))]
                public string Code { get; set; } = "";
                public bool IsValidCode(string value) => value.StartsWith("W", System.StringComparison.Ordinal);
                public bool EnabledCheck() => IsEnabled;
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("instance.EnabledCheck() &&", generated, StringComparison.Ordinal);
        Assert.Contains("!instance.IsValidCode(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ForwardsScoped_ToValidator()
    {
        var source = """
            using ZValidation;
            namespace ZeroAlloc.Inject { public sealed class ScopedAttribute : System.Attribute {} }
            namespace TestModels;
            [Validate, global::ZeroAlloc.Inject.Scoped]
            public class Customer { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("[global::ZeroAlloc.Inject.ScopedAttribute]", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ForwardsTransient_ToValidator()
    {
        var source = """
            using ZValidation;
            namespace ZeroAlloc.Inject { public sealed class TransientAttribute : System.Attribute {} }
            namespace TestModels;
            [Validate, global::ZeroAlloc.Inject.Transient]
            public class Order { [NotEmpty] public string Ref { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("[global::ZeroAlloc.Inject.TransientAttribute]", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ForwardsSingleton_ToValidator()
    {
        var source = """
            using ZValidation;
            namespace ZeroAlloc.Inject { public sealed class SingletonAttribute : System.Attribute {} }
            namespace TestModels;
            [Validate, global::ZeroAlloc.Inject.Singleton]
            public class Country { [NotEmpty] public string Code { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("[global::ZeroAlloc.Inject.SingletonAttribute]", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NoLifetime_EmitsNoLifetimeAttribute()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Plain { [NotEmpty] public string Value { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.DoesNotContain("ZeroAlloc.Inject", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsConstructorParam_ForNestedValidateType()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
            [Validate] public class Customer { public Address Home { get; set; } = new(); }
            """;

        var customerSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("CustomerValidator", StringComparison.Ordinal));

        Assert.Contains("AddressValidator homeValidator", customerSource, StringComparison.Ordinal);
        Assert.Contains("_homeValidator", customerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsConstructorParam_ForCollectionOfValidateType()
    {
        var source = """
            using ZValidation;
            using System.Collections.Generic;
            namespace TestModels;
            [Validate] public class Item { [NotEmpty] public string Name { get; set; } = ""; }
            [Validate] public class Bag { public List<Item> Things { get; set; } = new(); }
            """;

        var bagSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("BagValidator", StringComparison.Ordinal));

        Assert.Contains("ItemValidator thingsValidator", bagSource, StringComparison.Ordinal);
        Assert.Contains("_thingsValidator", bagSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NoConstructor_WhenNoNestedProperties()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate] public class Plain { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);

        Assert.DoesNotContain("public PlainValidator(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_TwoNestedProperties_SameType_TwoDistinctParams()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
            [Validate] public class Order
            {
                public Address Shipping { get; set; } = new();
                public Address Billing  { get; set; } = new();
            }
            """;

        var orderSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("OrderValidator", StringComparison.Ordinal));

        Assert.Contains("_shippingValidator", orderSource, StringComparison.Ordinal);
        Assert.Contains("_billingValidator", orderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ValidateWith_SingleProperty_UsesSpecifiedValidatorType()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            public class Money { public decimal Amount { get; set; } }
            [Validate]
            public class MoneyValidator : ValidatorFor<Money>
            {
                public override global::ZValidation.ValidationResult Validate(Money instance) =>
                    new(new global::ZValidation.ValidationFailure[0]);
            }
            [Validate]
            public class Invoice
            {
                [ValidateWith(typeof(MoneyValidator))]
                public Money Total { get; set; } = new();
            }
            """;

        var invoiceSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("InvoiceValidator", StringComparison.Ordinal));

        Assert.Contains("MoneyValidator", invoiceSource, StringComparison.Ordinal);
        Assert.Contains("_totalValidator", invoiceSource, StringComparison.Ordinal);
        Assert.Contains("\"Total.\" +", invoiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ValidateWith_CollectionProperty_UsesSpecifiedValidatorType()
    {
        var source = """
            using ZValidation;
            using System.Collections.Generic;
            namespace TestModels;
            public class Tag { public string Name { get; set; } = ""; }
            [Validate]
            public class TagValidator : ValidatorFor<Tag>
            {
                public override global::ZValidation.ValidationResult Validate(Tag instance) =>
                    new(new global::ZValidation.ValidationFailure[0]);
            }
            [Validate]
            public class Article
            {
                [ValidateWith(typeof(TagValidator))]
                public List<Tag> Tags { get; set; } = new();
            }
            """;

        var articleSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("ArticleValidator", StringComparison.Ordinal));

        Assert.Contains("TagValidator", articleSource, StringComparison.Ordinal);
        Assert.Contains("_tagsValidator", articleSource, StringComparison.Ordinal);
        Assert.Contains("\"Tags[\" +", articleSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyzer_ZV0011_Fires_WhenValidateWithOnAlreadyValidatedType()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
            [Validate] public class Customer
            {
                [ValidateWith(typeof(AddressValidator))]
                public Address Home { get; set; } = new();
            }
            """;

        var diagnostics = RunGeneratorGetDiagnostics(source);
        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZV0011", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyzer_ZV0012_Fires_WhenValidateWithTypeDoesNotMatchProperty()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate] public class Address { [NotEmpty] public string Street { get; set; } = ""; }
            [Validate] public class Name    { [NotEmpty] public string Value  { get; set; } = ""; }
            [Validate] public class Customer
            {
                [ValidateWith(typeof(NameValidator))]
                public Address Home { get; set; } = new();
            }
            """;

        var diagnostics = RunGeneratorGetDiagnostics(source);
        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZV0012", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_EmitsErrorCode_InFailureInitializer()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [NotEmpty(ErrorCode = "NAME_REQUIRED")] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("ErrorCode = \"NAME_REQUIRED\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsSeverity_Warning_InFailureInitializer()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [NotEmpty(Severity = Severity.Warning)] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("global::ZValidation.Severity.Warning", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_OmitsErrorCode_WhenNull()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.DoesNotContain("ErrorCode", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_OmitsSeverity_WhenError()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.DoesNotContain("Severity", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EscapesSpecialChars_InErrorCode()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Item { [NotEmpty(ErrorCode = "CODE\"WITH\"QUOTES")] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("CODE\\\"WITH\\\"QUOTES", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NestedPropagation_ForwardsErrorCodeAndSeverity()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Inner { [NotEmpty(ErrorCode = "E1", Severity = Severity.Warning)] public string Val { get; set; } = ""; }
            [Validate]
            public class Outer { public Inner Child { get; set; } = new(); }
            """;

        var outerSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("OuterValidator", StringComparison.Ordinal));

        Assert.Contains("f.ErrorCode", outerSource, StringComparison.Ordinal);
        Assert.Contains("f.Severity", outerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_CollectionPropagation_ForwardsErrorCodeAndSeverity()
    {
        var source = """
            using ZValidation;
            using System.Collections.Generic;
            namespace TestModels;
            [Validate]
            public class Item { [NotEmpty(ErrorCode = "E1")] public string Name { get; set; } = ""; }
            [Validate]
            public class Bag { public List<Item> Items { get; set; } = new(); }
            """;

        var bagSource = RunGeneratorGetSources(source)
            .First(s => s.Contains("BagValidator", StringComparison.Ordinal));

        Assert.Contains("f.ErrorCode", bagSource, StringComparison.Ordinal);
        Assert.Contains("f.Severity", bagSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsMatches_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [Matches(@"^\d{5}$")] public string Zip { get; set; } = ""; }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("Regex.IsMatch", generated, StringComparison.Ordinal);
        // EscapeString in the generator converts \ to \\, so the emitted literal contains ^\\d{5}$
        Assert.Contains(@"^\\d{5}$", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsEmailAddress_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [EmailAddress] public string Email { get; set; } = ""; }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("EmailValidator.IsValid", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_EmitsInclusiveBetween_Check()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [InclusiveBetween(1, 10)] public int Value { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("< 1", generated, StringComparison.Ordinal);
        Assert.Contains("> 10", generated, StringComparison.Ordinal);
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

    private static System.Collections.Generic.IReadOnlyList<Diagnostic> RunGeneratorGetDiagnostics(string source)
    {
        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().Diagnostics;
    }

    private static string RunGeneratorGetSource(string source)
    {
        // Include System.Runtime so Roslyn can fully resolve attribute constructor argument types.
        var systemRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(systemRuntime, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var result = driver.GetRunResult();
        return result.GeneratedTrees[0].ToString();
    }

    private static IReadOnlyList<string> RunGeneratorGetSources(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValidateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(
                    System.IO.Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ValidatorGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
    }

    [Fact]
    public void Generator_PropertyValue_NonNullableValueType_EmitsInterpolatedAccess()
    {
        // int property → Convert.ToString with InvariantCulture for locale-safe formatting
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [GreaterThan(0, Message = "Must be > 0, got {PropertyValue}.")] public int Age { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("{System.Convert.ToString(instance.Age, System.Globalization.CultureInfo.InvariantCulture)}", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_PropertyValue_String_EmitsNullCoalesce()
    {
        // string property → {instance.Name ?? "null"}
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [MaxLength(5, Message = "Got {PropertyValue}.")] public string Name { get; set; } = ""; }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("instance.Name ?? \"null\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_PropertyValue_NullableValueType_EmitsNullableToString()
    {
        // int? property → null-guarded Convert.ToString with InvariantCulture
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [GreaterThan(0, Message = "Got {PropertyValue}.")] public int? Score { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("instance.Score is null ? \"null\" : System.Convert.ToString(instance.Score.Value, System.Globalization.CultureInfo.InvariantCulture)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_PropertyValue_MixedWithCompileTimePlaceholders()
    {
        // Both {PropertyName} (compile-time) and {PropertyValue} (runtime) in same message
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [GreaterThan(0, Message = "{PropertyName} must be > 0, got {PropertyValue}.")] public int Age { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        // {PropertyName} is substituted at code-gen time → "Age" appears as literal
        Assert.Contains("Age must be > 0, got ", generated, StringComparison.Ordinal);
        // {PropertyValue} becomes interpolation hole with invariant-culture formatting
        Assert.Contains("{System.Convert.ToString(instance.Age, System.Globalization.CultureInfo.InvariantCulture)}", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_PropertyValue_NotInMessage_EmitsPlainStringLiteral()
    {
        // Regression guard: no {PropertyValue} → no interpolated string emitted
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Foo { [GreaterThan(0, Message = "Must be positive.")] public int Age { get; set; } }
            """;
        var generated = RunGeneratorGetSource(source);
        Assert.Contains("\"Must be positive.\"", generated, StringComparison.Ordinal);
        // Should NOT be an interpolated string
        Assert.DoesNotContain("$\"Must be positive.\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_DisplayName_UsesDisplayNameInDefaultMessage()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [DisplayName("First Name")]
                [NotEmpty]
                public string Forename { get; set; } = "";
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("\"First Name must not be empty.\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Forename must not be empty.\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_DisplayName_SubstitutesPropertyNamePlaceholder()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [DisplayName("ZIP Code")]
                [Matches(@"^\d{5}$", Message = "{PropertyName} must be 5 digits.")]
                public string ZipCode { get; set; } = "";
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("\"ZIP Code must be 5 digits.\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ZipCode must be 5 digits.\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NoDisplayName_UsesRawPropertyName()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [NotEmpty]
                public string Forename { get; set; } = "";
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("\"Forename must not be empty.\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ValidatorStop_FlatPath_EmitsCountCheckAfterEachPropertyGroup()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate(StopOnFirstFailure = true)]
            public class M
            {
                [NotEmpty]
                public string Name { get; set; } = "";

                [GreaterThan(0)]
                public int Age { get; set; }
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.Contains("_b0 = count", generated, StringComparison.Ordinal);
        Assert.Contains("count > _b0", generated, StringComparison.Ordinal);
        Assert.Contains("_b1 = count", generated, StringComparison.Ordinal);
        Assert.Contains("count > _b1", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ValidatorStop_NestedPath_EmitsFailuresCountCheckAfterEachPropertyGroup()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Inner { [NotEmpty] public string X { get; set; } = ""; }

            [Validate(StopOnFirstFailure = true)]
            public class Outer
            {
                [NotEmpty]
                public string Reference { get; set; } = "";

                public Inner? Item { get; set; }
            }
            """;

        var generated = RunGeneratorGetSources(source)
            .First(s => s.Contains("class OuterValidator"));
        Assert.Contains("_b0 = _buf.Count", generated, StringComparison.Ordinal);
        Assert.Contains("_buf.Count > _b0", generated, StringComparison.Ordinal);
        Assert.Contains("_b1 = _buf.Count", generated, StringComparison.Ordinal);
        Assert.Contains("_buf.Count > _b1", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ValidatorStop_Default_NoCountChecks()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class M
            {
                [NotEmpty]
                public string Name { get; set; } = "";

                [GreaterThan(0)]
                public int Age { get; set; }
            }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.DoesNotContain("_b0 =", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("_b1 =", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_MixedPath_UsesFailureBuffer_NotList()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class Inner { [NotEmpty] public string X { get; set; } = ""; }
            [Validate]
            public class Outer { public Inner? Item { get; set; } }
            """;

        var generated = RunGeneratorGetSources(source)
            .First(s => s.Contains("class OuterValidator"));
        Assert.Contains("FailureBuffer", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("List<global::ZValidation.ValidationFailure>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_FlatPath_DoesNotUseFailureBuffer()
    {
        var source = """
            using ZValidation;
            namespace TestModels;
            [Validate]
            public class M { [NotEmpty] public string Name { get; set; } = ""; }
            """;

        var generated = RunGeneratorGetSource(source);
        Assert.DoesNotContain("FailureBuffer", generated, StringComparison.Ordinal);
    }
}
