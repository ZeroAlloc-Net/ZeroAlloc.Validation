using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class MustTests
{
    private readonly MustModelValidator _validator = new();

    [Fact]
    public void Must_ValidValue_Passes()
    {
        ValidationAssert.NoErrors(_validator.Validate(new MustModel { Code = "WIDGET" }));
    }

    [Fact]
    public void Must_InvalidValue_Fails()
    {
        ValidationAssert.HasError(_validator.Validate(new MustModel { Code = "INVALID" }), "Code");
    }

    [Fact]
    public void Must_CustomMessage_Propagated()
    {
        var result = _validator.Validate(new MustModel { Code = "INVALID" });
        ValidationAssert.HasErrorWithMessage(result, "Code", "Code must start with W");
    }
}
