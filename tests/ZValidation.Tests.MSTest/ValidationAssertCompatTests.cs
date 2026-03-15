using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests.MSTest;

[TestClass]
public class ValidationAssertCompatTests
{
    [TestMethod]
    public void HasError_WorksWithMSTest()
    {
        var failure = new ValidationFailure { PropertyName = "Email", ErrorMessage = "Invalid" };
        var result = new ValidationResult([failure]);
        ValidationAssert.HasError(result, "Email");
    }

    [TestMethod]
    public void NoErrors_WorksWithMSTest()
    {
        var result = new ValidationResult([]);
        ValidationAssert.NoErrors(result);
    }
}
