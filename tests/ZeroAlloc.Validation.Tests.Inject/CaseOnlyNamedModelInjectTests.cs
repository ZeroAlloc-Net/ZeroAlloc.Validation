using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Inject;

public class CaseOnlyNamedModelInjectTests
{
    [Fact]
    public void AddZeroAllocValidators_ResolvesValidatorWithCaseOnlyNestedProperties()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocValidators();
        var sp = services.BuildServiceProvider();

        var validator = sp.GetRequiredService<ValidatorFor<CaseOnlyNamedModel>>();

        var valid = new ApiKeyOptions { Key = "k", Expiry = 1 };
        var invalid = new ApiKeyOptions { Key = "", Expiry = 1 };
        Assert.True(validator.Validate(new CaseOnlyNamedModel { Address = valid, address = valid }).IsValid);

        var result = validator.Validate(new CaseOnlyNamedModel { Address = valid, address = invalid });
        Assert.False(result.IsValid);
        Assert.Equal("address.Key", result.Failures[0].PropertyName);
    }
}
