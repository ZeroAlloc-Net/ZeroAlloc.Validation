using System.Reflection;
using Xunit;
using ZValidation;

namespace ZValidation.Tests.Attributes;

public class AttributeDeclarationTests
{
    [Fact]
    public void ValidateAttribute_CanBeAppliedToClass()
    {
        var attrs = typeof(SampleModel).GetCustomAttributes(typeof(ValidateAttribute), false);
#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        Assert.Single(attrs);
#pragma warning restore HLQ005
    }

    [Fact]
    public void NotNullAttribute_CanBeAppliedToProperty()
    {
        var prop = typeof(NullModel).GetProperty(nameof(NullModel.Value))!;
        var attr = prop.GetCustomAttribute<NotNullAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void NotEmptyAttribute_MessageDefaultsToNull()
    {
        var attr = new NotEmptyAttribute();
        Assert.Null(attr.Message);
    }

    [Fact]
    public void NotEmptyAttribute_CanSetCustomMessage()
    {
        var attr = new NotEmptyAttribute { Message = "Required" };
        Assert.Equal("Required", attr.Message);
    }

    [Fact]
    public void MinLengthAttribute_StoresMinValue()
    {
        var attr = new ZValidation.MinLengthAttribute(3);
        Assert.Equal(3, attr.Min);
    }

    [Fact]
    public void MaxLengthAttribute_StoresMaxValue()
    {
        var attr = new ZValidation.MaxLengthAttribute(100);
        Assert.Equal(100, attr.Max);
    }

    [Fact]
    public void GreaterThanAttribute_StoresValue()
    {
        var attr = new GreaterThanAttribute(0);
        Assert.Equal(0.0, attr.Value);
    }

    [Fact]
    public void LessThanAttribute_StoresValue()
    {
        var attr = new LessThanAttribute(120);
        Assert.Equal(120.0, attr.Value);
    }

    [Fact]
    public void InclusiveBetweenAttribute_StoresMinAndMax()
    {
        var attr = new InclusiveBetweenAttribute(1, 100);
        Assert.Equal(1.0, attr.Min);
        Assert.Equal(100.0, attr.Max);
    }

    [Fact]
    public void EmailAddressAttribute_CanBeCreated()
    {
        var attr = new ZValidation.EmailAddressAttribute();
        Assert.Null(attr.Message);
    }

    [Fact]
    public void MatchesAttribute_StoresPattern()
    {
        var attr = new MatchesAttribute(@"^\d{4}$");
        Assert.Equal(@"^\d{4}$", attr.Pattern);
    }

    [Fact]
    public void NotNullAttribute_MessageDefaultsToNull()
    {
        var attr = new NotNullAttribute();
        Assert.Null(attr.Message);
    }

    [Fact]
    public void MinLengthAttribute_MessageDefaultsToNull()
    {
        // FQN required: System.ComponentModel.DataAnnotations also defines MinLengthAttribute
        var attr = new ZValidation.MinLengthAttribute(3);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void MinLengthAttribute_CanSetCustomMessage()
    {
        var attr = new ZValidation.MinLengthAttribute(3) { Message = "Too short" };
        Assert.Equal("Too short", attr.Message);
    }

    [Fact]
    public void MaxLengthAttribute_MessageDefaultsToNull()
    {
        // FQN required: System.ComponentModel.DataAnnotations also defines MaxLengthAttribute
        var attr = new ZValidation.MaxLengthAttribute(100);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void MaxLengthAttribute_CanSetCustomMessage()
    {
        var attr = new ZValidation.MaxLengthAttribute(100) { Message = "Too long" };
        Assert.Equal("Too long", attr.Message);
    }

    [Fact]
    public void GreaterThanAttribute_MessageDefaultsToNull()
    {
        var attr = new GreaterThanAttribute(0);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void LessThanAttribute_MessageDefaultsToNull()
    {
        var attr = new LessThanAttribute(120);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void InclusiveBetweenAttribute_MessageDefaultsToNull()
    {
        var attr = new InclusiveBetweenAttribute(1, 100);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void MatchesAttribute_MessageDefaultsToNull()
    {
        var attr = new MatchesAttribute(@"^\d{4}$");
        Assert.Null(attr.Message);
    }

    [Fact]
    public void EqualAttribute_NumericConstructor_StoresValue()
    {
        var attr = new EqualAttribute(42.0);
        Assert.Equal(42.0, attr.NumericValue);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void EqualAttribute_StringConstructor_StoresValue()
    {
        var attr = new EqualAttribute("active");
        Assert.Equal("active", attr.StringValue);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void NotEqualAttribute_NumericConstructor_StoresValue()
    {
        var attr = new NotEqualAttribute(0.0);
        Assert.Equal(0.0, attr.NumericValue);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void NotEqualAttribute_StringConstructor_StoresValue()
    {
        var attr = new NotEqualAttribute("inactive");
        Assert.Equal("inactive", attr.StringValue);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void GreaterThanOrEqualToAttribute_StoresValue()
    {
        var attr = new GreaterThanOrEqualToAttribute(5.0);
        Assert.Equal(5.0, attr.Value);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void LessThanOrEqualToAttribute_StoresValue()
    {
        var attr = new LessThanOrEqualToAttribute(100.0);
        Assert.Equal(100.0, attr.Value);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void ExclusiveBetweenAttribute_StoresMinMax()
    {
        var attr = new ExclusiveBetweenAttribute(1.0, 9.0);
        Assert.Equal(1.0, attr.Min);
        Assert.Equal(9.0, attr.Max);
        Assert.Null(attr.Message);
    }

    [Fact]
    public void LengthAttribute_StoresMinMax()
    {
        var attr = new LengthAttribute(2, 50);
        Assert.Equal(2, attr.Min);
        Assert.Equal(50, attr.Max);
        Assert.Null(attr.Message);
    }

    [Validate]
    private class SampleModel { }

    private class NullModel
    {
        [NotNull]
        public string? Value { get; set; }
    }
}
