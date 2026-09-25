using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Inject;

// Issue #246: AddZeroAllocValidators alone builds a validator that composes other validators.
public class ComposedValidatorInjectTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocValidators();
        // ValidateOnBuild fails the build for any registration the container cannot construct,
        // so an unresolvable nested validator is caught here rather than on first use.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static Warehouse ValidWarehouse() => new()
    {
        Name = "North",
        Primary = new ApiKeyOptions { Key = "k", Expiry = 1 },
        Keys = [new ApiKeyOptions { Key = "a", Expiry = 1 }, new ApiKeyOptions { Key = "b", Expiry = 2 }],
        Dock = new Dock { Number = 3 },
    };

    [Fact]
    public void AddZeroAllocValidators_ResolvesValidatorComposingNestedCollectionAndValidateWith()
    {
        using var sp = BuildProvider();

        var validator = sp.GetRequiredService<ValidatorFor<Warehouse>>();

        Assert.True(validator.Validate(ValidWarehouse()).IsValid);

        var invalid = ValidWarehouse();
        invalid.Primary = new ApiKeyOptions { Key = "", Expiry = 1 };
        invalid.Keys[1] = new ApiKeyOptions { Key = "b", Expiry = 0 };
        invalid.Dock = new Dock { Number = 0 };

        var result = validator.Validate(invalid);
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Failures.Length);
        Assert.Equal("Primary.Key", result.Failures[0].PropertyName);
        Assert.Equal("Dock.Number", result.Failures[1].PropertyName);
        Assert.Equal("Keys[1].Expiry", result.Failures[2].PropertyName);
    }

    [Fact]
    public void AddZeroAllocValidators_ResolvesValidatorComposingAComposedValidator()
    {
        using var sp = BuildProvider();

        var validator = sp.GetRequiredService<ValidatorFor<Site>>();

        Assert.True(validator.Validate(new Site { Main = ValidWarehouse() }).IsValid);

        var invalid = ValidWarehouse();
        invalid.Dock = new Dock { Number = -1 };
        var result = validator.Validate(new Site { Main = invalid });
        Assert.False(result.IsValid);
        Assert.Equal("Main.Dock.Number", result.Failures[0].PropertyName);
    }

    [Fact]
    public void AddZeroAllocValidators_ComposesTheRegisteredValidatorFor()
    {
        // The composed validator takes its nested validators as ValidatorFor<T>, so it shares
        // the one registration a caller would resolve on its own, and a replacement registered
        // before AddZeroAllocValidators is what it composes.
        var services = new ServiceCollection();
        var replacement = new RejectAllApiKeysValidator();
        services.AddSingleton<ValidatorFor<ApiKeyOptions>>(replacement);
        services.AddZeroAllocValidators();
        using var custom = services.BuildServiceProvider();

        var result = custom.GetRequiredService<ValidatorFor<Warehouse>>().Validate(ValidWarehouse());
        Assert.False(result.IsValid);
        Assert.Equal("Primary.Key", result.Failures[0].PropertyName);
    }

    private sealed class RejectAllApiKeysValidator : ValidatorFor<ApiKeyOptions>
    {
        public override ValidationResult Validate(ApiKeyOptions instance) =>
            new([new ValidationFailure { PropertyName = "Key", ErrorMessage = "Rejected." }]);
    }
}
