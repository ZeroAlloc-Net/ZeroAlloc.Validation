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
    public void Generator_EmitsStopAtFirstFailure_AsElseIf()
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
        Assert.Contains("else if", generated, StringComparison.Ordinal);
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
    public void Generator_UsesListForModelWithNestedValidateType()
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

        Assert.Contains("List<", customerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("List<", RunGeneratorGetSources(source)
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
    public void Generator_UsesListForModelWithCollectionOfValidateType()
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

        Assert.Contains("List<", orderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("List<", RunGeneratorGetSources(source)
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
        Assert.Contains("List<", articleSource, StringComparison.Ordinal);
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
}
