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
        Assert.Single(attrs);
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

    [Validate]
    private class SampleModel { }

    private class NullModel
    {
        [NotNull]
        public string? Value { get; set; }
    }
}
