using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Inject;

public class InjectIntegrationTests
{
    [Fact]
    public void AddZeroAllocValidators_RegistersValidatorForT_InDI()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocValidators();

        var sp        = services.BuildServiceProvider();
        var validator = sp.GetService<ValidatorFor<ApiKeyOptions>>();

        Assert.NotNull(validator);
    }

    [Fact]
    public void AddZeroAllocValidators_ValidModel_PassesValidation()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocValidators();

        var sp        = services.BuildServiceProvider();
        var validator = sp.GetRequiredService<ValidatorFor<ApiKeyOptions>>();
        var result    = validator.Validate(new ApiKeyOptions { Key = "abc123", Expiry = 30 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AddZeroAllocValidators_InvalidModel_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocValidators();

        var sp        = services.BuildServiceProvider();
        var validator = sp.GetRequiredService<ValidatorFor<ApiKeyOptions>>();
        var result    = validator.Validate(new ApiKeyOptions { Key = "", Expiry = 0 });

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Failures.Length);
    }

    [Fact]
    public void AddZeroAllocValidators_CalledTwice_NoDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocValidators();
        services.AddZeroAllocValidators(); // second call — TryAdd should be a no-op

        var sp         = services.BuildServiceProvider();
        var validators = sp.GetServices<ValidatorFor<ApiKeyOptions>>();

        #pragma warning disable HLQ005
        Assert.Single(validators);
        #pragma warning restore HLQ005
    }
}
