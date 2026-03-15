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

    [Validate]
    private class SampleModel { }

    private class NullModel
    {
        [NotNull]
        public string? Value { get; set; }
    }
}
