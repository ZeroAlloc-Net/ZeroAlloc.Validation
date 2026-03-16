using NUnit.Framework;
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests.NUnit;

[TestFixture]
public class ValidationAssertCompatTests
{
    [Test]
    public void HasError_WorksWithNUnit()
    {
        var failure = new ValidationFailure { PropertyName = "Email", ErrorMessage = "Invalid" };
        var result = new ValidationResult([failure]);
        ValidationAssert.HasError(result, "Email");
    }

    [Test]
    public void NoErrors_WorksWithNUnit()
    {
        var result = new ValidationResult([]);
        ValidationAssert.NoErrors(result);
    }
}
