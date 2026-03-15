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

    [Validate]
    private class SampleModel { }
}
