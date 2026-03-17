using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

// Uses existing PersonValidator (from Person.cs which has [Validate])
public class PipelineBehaviorTests
{
    [Fact]
    public async Task ValidateAsync_NoBehaviors_ReturnsSameResultAsValidate()
    {
        var validator = new PersonValidator();
        var person = new Person { Name = "Alice", Email = "alice@example.com", Age = 30 };

        var syncResult  = validator.Validate(person);
        var asyncResult = await validator.ValidateAsync(person);

        Assert.Equal(syncResult.IsValid, asyncResult.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_NoBehaviors_InvalidModel_ReturnsSameFailures()
    {
        var validator = new PersonValidator();
        var person = new Person { Name = "", Email = "alice@example.com", Age = 30 };

        var asyncResult = await validator.ValidateAsync(person);

        Assert.False(asyncResult.IsValid);
        ValidationAssert.HasError(asyncResult, "Name");
    }
}
